package com.marsxz.marsxzmedia

import org.schabi.newpipe.extractor.NewPipe
import org.schabi.newpipe.extractor.ServiceList
import org.schabi.newpipe.extractor.localization.Localization
import org.schabi.newpipe.extractor.stream.AudioStream
import org.schabi.newpipe.extractor.stream.StreamInfo
import org.schabi.newpipe.extractor.stream.VideoStream
import java.util.Locale
import kotlin.math.abs

object YoutubeFormatsService {
    data class SelectedVideoSource(
        val stream: VideoStream,
        val sourceHeight: Int?,
        val isVideoOnly: Boolean
    )

    @Volatile
    private var initialized = false

    private fun ensureInitialized() {
        if (!initialized) {
            // Инициализация с русской локализацией для правильного парсинга
            NewPipe.init(DownloaderImpl(), Localization("ru", "RU"))
            initialized = true
        }
    }

    fun loadFormats(url: String): Result<VideoFormatsInfo> {
        return try {
            val info = loadInfoOrThrow(url)

            // DASH потоки содержат 1080p, 1440p, 2160p
            val dashStreams = info.videoOnlyStreams ?: emptyList()
            // Muxed потоки обычно только до 360p/720p
            val muxedStreams = info.videoStreams ?: emptyList()

            val heights = buildList {
                addAll(dashStreams.mapNotNull { parseHeight(it.resolution ?: "") })
                addAll(muxedStreams.mapNotNull { parseHeight(it.resolution ?: "") })
            }.distinct().filter { it > 0 }.sortedDescending()

            if (heights.isEmpty()) {
                // Если стандартный парсинг провалился, попробуем вытащить любые цифры
                val fallback = (dashStreams + muxedStreams)
                    .mapNotNull { it.resolution?.filter { c -> c.isDigit() }?.take(4)?.toIntOrNull() }
                    .distinct().filter { it > 100 }.sortedDescending()
                
                if (fallback.isEmpty()) throw IllegalStateException("YouTube не вернул доступных видео-форматов")
                
                return buildResult(info, fallback)
            }

            buildResult(info, heights)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    private fun buildResult(info: StreamInfo, heights: List<Int>): Result<VideoFormatsInfo> {
        val qualityItems = heights.map { h -> VideoFormatsInfo.QualityItem(heightToLabel(h), h) }

        val groupedAudio = linkedMapOf<String, String>()
        for (stream in info.audioStreams) {
            val label = buildAudioTrackLabel(stream)
            val key = buildAudioTrackKey(stream, label)
            groupedAudio.putIfAbsent(key, label)
        }

        val audioLabels = groupedAudio.values.sortedWith(
            compareBy<String> { !it.contains("Оригинал", ignoreCase = true) }
                .thenBy { it.contains("Дубляж", ignoreCase = true) }
                .thenBy { it.lowercase(Locale.ROOT) }
        )

        return Result.success(
            VideoFormatsInfo(
                maxVideoHeight = heights.maxOrNull(),
                qualityItems = qualityItems,
                audioTracks = audioLabels
            )
        )
    }

    fun loadRawInfo(url: String): Result<StreamInfo> {
        return try {
            Result.success(loadInfoOrThrow(url))
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    private fun loadInfoOrThrow(url: String): StreamInfo {
        ensureInitialized()
        return StreamInfo.getInfo(ServiceList.YouTube, url)
    }

    fun selectVideoSource(info: StreamInfo, selectedQualityLabel: String?): SelectedVideoSource? {
        val candidates = buildList {
            addAll(info.videoOnlyStreams.map { SelectedVideoSource(it, parseHeight(it.resolution ?: ""), true) })
            addAll(info.videoStreams.map { SelectedVideoSource(it, parseHeight(it.resolution ?: ""), false) })
        }.filter { it.stream.content?.isNotBlank() == true }

        if (candidates.isEmpty()) return null

        val targetHeight = parseHeight(selectedQualityLabel ?: "")
        if (targetHeight == null) {
            return candidates.maxWithOrNull(
                compareBy<SelectedVideoSource> { it.sourceHeight ?: 0 }
                    .thenBy { if (it.isVideoOnly) 1 else 0 }
                    .thenBy { it.stream.bitrate }
            )
        }

        return candidates.minWithOrNull(
            compareBy<SelectedVideoSource> {
                val height = it.sourceHeight ?: 0
                when {
                    height == targetHeight -> 0
                    height > targetHeight -> 1
                    else -> 2
                }
            }.thenBy {
                val height = it.sourceHeight ?: 0
                abs(height - targetHeight)
            }.thenBy {
                if (it.isVideoOnly) 0 else 1 
            }.thenByDescending {
                it.stream.bitrate
            }
        )
    }

    fun selectAudioStream(
        info: StreamInfo,
        selectedTrackLabel: String?,
        selectedBitrateKbps: Int?
    ): AudioStream? {
        val normalizedSelected = selectedTrackLabel?.trim().orEmpty()
        val matchingTrackStreams = info.audioStreams.filter {
            buildAudioTrackLabel(it).equals(normalizedSelected, ignoreCase = true)
        }
        val pool = if (matchingTrackStreams.isNotEmpty()) matchingTrackStreams else info.audioStreams
        if (pool.isEmpty()) return null

        if (selectedBitrateKbps == null) {
            return pool.maxByOrNull { audioKbps(it) }
        }

        return pool.minByOrNull { abs(audioKbps(it) - selectedBitrateKbps) }
            ?: pool.maxByOrNull { audioKbps(it) }
    }

    fun buildAudioTrackLabel(stream: AudioStream): String {
        val locale = stream.audioLocale
        val langCode = locale?.language?.takeIf { it.isNotBlank() }.orEmpty()
        val displayLanguage = if (langCode.isNotBlank()) {
            locale!!.getDisplayLanguage(Locale("ru")).replaceFirstChar { c ->
                if (c.isLowerCase()) c.titlecase(Locale("ru")) else c.toString()
            }
        } else ""

        val trackName = stream.audioTrackName?.trim().orEmpty()
        val trackType = stream.audioTrackType?.name?.trim().orEmpty()

        return buildString {
            if (displayLanguage.isNotBlank()) append(displayLanguage)
            else if (trackName.isNotBlank()) append(trackName)
            else append("Неизвестная дорожка")

            if (trackName.isNotBlank() && !trackName.equals(displayLanguage, ignoreCase = true)) {
                append(" (")
                append(trackName)
                append(')')
            }

            when (trackType.uppercase(Locale.ROOT)) {
                "ORIGINAL" -> append(" • Оригинал")
                "DUBBED" -> append(" • Дубляж")
                "DESCRIPTIVE" -> append(" • Описание")
            }
        }
    }

    private fun buildAudioTrackKey(stream: AudioStream, fallback: String): String {
        val locale = stream.audioLocale
        val langCode = locale?.language?.takeIf { it.isNotBlank() }.orEmpty()
        val trackName = stream.audioTrackName?.trim().orEmpty()
        val trackId = stream.audioTrackId?.trim().orEmpty()
        val trackType = stream.audioTrackType?.name?.trim().orEmpty()

        return when {
            trackId.isNotBlank() -> "id:$trackId"
            langCode.isNotBlank() && trackName.isNotBlank() -> "langname:$langCode:${trackName.lowercase(Locale.ROOT)}:$trackType"
            langCode.isNotBlank() -> "lang:$langCode:$trackType"
            trackName.isNotBlank() -> "name:${trackName.lowercase(Locale.ROOT)}:$trackType"
            else -> "fallback:${fallback.lowercase(Locale.ROOT)}"
        }
    }

    fun audioKbps(stream: AudioStream): Int {
        val avg = stream.averageBitrate
        if (avg > 1000) return avg / 1000
        if (avg > 0) return avg
        val br = stream.bitrate
        return if (br > 1000) br / 1000 else br
    }

    fun parseHeight(resolutionOrLabel: String): Int? {
        if (resolutionOrLabel.isBlank()) return null
        
        // 1. Ищем формат 1080p или 1080p60
        val pMatch = Regex("""(\d{3,5})p""", RegexOption.IGNORE_CASE).find(resolutionOrLabel)
        if (pMatch != null) return pMatch.groupValues[1].toIntOrNull()
        
        // 2. Ищем формат 1920x1080
        val xMatch = Regex("""x(\d{3,5})""", RegexOption.IGNORE_CASE).find(resolutionOrLabel)
        if (xMatch != null) return xMatch.groupValues[1].toIntOrNull()
        
        // 3. Просто цифры (если пришло только "1080")
        val digits = resolutionOrLabel.filter { it.isDigit() }
        if (digits.length in 3..4) return digits.toIntOrNull()
        
        return null
    }

    fun heightToLabel(height: Int): String = when (height) {
        4320 -> "4320p (8K)"
        2160 -> "2160p (4K)"
        1440 -> "1440p (2K)"
        1080 -> "1080p (FHD)"
        720 -> "720p (HD)"
        480 -> "480p (SD)"
        360 -> "360p (SD)"
        240 -> "240p (SD)"
        144 -> "144p (SD)"
        else -> "${height}p"
    }
}
