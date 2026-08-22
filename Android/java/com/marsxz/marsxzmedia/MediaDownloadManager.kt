package com.marsxz.marsxzmedia

import android.content.Context
import org.schabi.newpipe.extractor.stream.AudioStream
import org.schabi.newpipe.extractor.stream.Stream
import org.schabi.newpipe.extractor.stream.VideoStream
import java.io.File
import java.io.FileOutputStream
import java.net.HttpURLConnection
import java.net.URL
import java.util.Locale
import java.util.concurrent.atomic.AtomicInteger
import kotlin.math.roundToInt

object MediaDownloadManager {
    private val notificationIds = AtomicInteger(3000)
    
    // User-Agent ДОЛЖЕН БЫТЬ ИДЕНТИЧЕН DownloaderImpl.DESKTOP_UA
    private const val USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36"

    data class Request(
        val url: String,
        val isAudio: Boolean,
        val title: String,
        val selectedQualityLabel: String?,
        val selectedBitrateLabel: String?,
        val selectedAudioTrackLabel: String?
    )

    fun download(context: Context, request: Request): Result<File> {
        val notificationId = notificationIds.incrementAndGet()

        return try {
            AppPaths.ensureDirectories(context)
            AppLog.write(context, "I", "Запуск загрузки: ${request.url}", "Download")

            val info = YoutubeFormatsService.loadRawInfo(request.url).getOrElse { throw it }

            val outFile = if (request.isAudio) {
                downloadAudio(context, request, info, notificationId)
            } else {
                downloadVideo(context, request, info, notificationId)
            }

            AppLog.write(context, "I", "Загрузка завершена: ${outFile.absolutePath}", "Download")

            DownloadNotificationHelper.showDownloadComplete(
                context = context,
                notificationId = notificationId,
                fileName = outFile.name,
                isAudio = request.isAudio,
                success = true
            )

            Result.success(outFile)
        } catch (e: Exception) {
            AppLog.write(context, "E", "Ошибка загрузки: ${e.message}", "Download")

            DownloadNotificationHelper.showDownloadComplete(
                context = context,
                notificationId = notificationId,
                fileName = request.title.ifBlank { "Файл" },
                isAudio = request.isAudio,
                success = false,
                message = e.message ?: "Неизвестная ошибка"
            )

            Result.failure(e)
        }
    }

    private fun downloadAudio(
        context: Context,
        request: Request,
        info: org.schabi.newpipe.extractor.stream.StreamInfo,
        notificationId: Int
    ): File {
        val bitrate = parseBitrateKbps(request.selectedBitrateLabel)
        val audioStream = YoutubeFormatsService.selectAudioStream(
            info,
            request.selectedAudioTrackLabel,
            bitrate
        ) ?: throw IllegalStateException("Не удалось подобрать аудиопоток")

        val sourceExt = audioExtension(audioStream)
        val tempFile = uniqueTargetFile(
            AppPaths.tempDir(context),
            "${request.title} [audio-src]",
            sourceExt
        )

        val totalAudioBytes = probeContentLength(audioStream.content)
        val tracker = CombinedDownloadTracker(
            totalVideoBytes = 0L,
            totalAudioBytes = totalAudioBytes
        )

        DownloadNotificationHelper.showDownloadStart(context, notificationId, request.title, true)

        downloadStreamToFile(
            context = context,
            notificationId = notificationId,
            fileTitle = request.title,
            stream = audioStream,
            file = tempFile,
            isAudio = true,
            stageText = "Скачивание аудио...",
            tracker = tracker,
            phase = DownloadPhase.AUDIO
        )

        DownloadNotificationHelper.updateDownloadProgress(
            context = context,
            notificationId = notificationId,
            fileName = request.title,
            isAudio = true,
            percent = 99.0,
            speed = "—",
            eta = "—",
            sizeInfo = "Конвертация в MP3..."
        )

        val translatedTitle = if (MediaFileNaming.shouldTranslateTitle(request.selectedAudioTrackLabel)) {
            val trackInfo = MediaFileNaming.parseTrackInfo(request.selectedAudioTrackLabel)
            GoogleHttpTranslator.translateTitleSync(request.title, trackInfo.languageCode)
        } else {
            null
        }

        val finalTitle = MediaFileNaming.buildFinalTitle(
            originalTitle = request.title,
            selectedAudioTrackLabel = request.selectedAudioTrackLabel,
            translatedTitle = translatedTitle
        )

        return MediaConverter.convertAudioToMp3(
            context = context,
            inputFile = tempFile,
            desiredTitle = finalTitle,
            requestedBitrateKbps = bitrate
        )
    }

    private fun downloadVideo(
        context: Context,
        request: Request,
        info: org.schabi.newpipe.extractor.stream.StreamInfo,
        notificationId: Int
    ): File {
        val targetHeight = YoutubeFormatsService.parseHeight(request.selectedQualityLabel ?: "")
        val selectedVideo = YoutubeFormatsService.selectVideoSource(
            info,
            request.selectedQualityLabel
        ) ?: throw IllegalStateException("Не удалось подобрать видеопоток")

        val selectedAudio = YoutubeFormatsService.selectAudioStream(
            info,
            request.selectedAudioTrackLabel,
            null
        )

        AppLog.write(
            context,
            "I",
            "Выбран видеопоток: source=${selectedVideo.sourceHeight}p, target=${targetHeight ?: selectedVideo.sourceHeight}p, videoOnly=${selectedVideo.isVideoOnly}",
            "Download"
        )

        val videoTempFile = uniqueTargetFile(
            AppPaths.tempDir(context),
            "${request.title} [video-src]",
            videoExtension(selectedVideo.stream)
        )

        val audioTempFile = selectedAudio?.let {
            uniqueTargetFile(
                AppPaths.tempDir(context),
                "${request.title} [audio-src]",
                audioExtension(it)
            )
        }

        val totalVideoBytes = probeContentLength(selectedVideo.stream.content)
        val totalAudioBytes = probeContentLength(selectedAudio?.content)

        val tracker = CombinedDownloadTracker(
            totalVideoBytes = totalVideoBytes,
            totalAudioBytes = totalAudioBytes
        )

        DownloadNotificationHelper.showDownloadStart(context, notificationId, request.title, false)

        downloadStreamToFile(
            context = context,
            notificationId = notificationId,
            fileTitle = request.title,
            stream = selectedVideo.stream,
            file = videoTempFile,
            isAudio = false,
            stageText = "Скачивание видео...",
            tracker = tracker,
            phase = DownloadPhase.VIDEO,
            extraLabel = request.selectedQualityLabel ?: "Видео"
        )

        if (selectedAudio != null && audioTempFile != null) {
            AppLog.write(
                context,
                "I",
                "Выбрана аудиодорожка для видео: ${request.selectedAudioTrackLabel ?: "Авто"} (${YoutubeFormatsService.audioKbps(selectedAudio)} kbps)",
                "Download"
            )

            downloadStreamToFile(
                context = context,
                notificationId = notificationId,
                fileTitle = request.title,
                stream = selectedAudio,
                file = audioTempFile,
                isAudio = false,
                stageText = "Скачивание аудио...",
                tracker = tracker,
                phase = DownloadPhase.AUDIO,
                extraLabel = request.selectedAudioTrackLabel ?: "Аудио"
            )
        }

        DownloadNotificationHelper.updateDownloadProgress(
            context = context,
            notificationId = notificationId,
            fileName = request.title,
            isAudio = false,
            percent = 99.0,
            speed = "—",
            eta = "—",
            sizeInfo = "Сборка файла..."
        )

        val translatedTitle = if (MediaFileNaming.shouldTranslateTitle(request.selectedAudioTrackLabel)) {
            val trackInfo = MediaFileNaming.parseTrackInfo(request.selectedAudioTrackLabel)
            GoogleHttpTranslator.translateTitleSync(request.title, trackInfo.languageCode)
        } else {
            null
        }

        val finalTitle = MediaFileNaming.buildFinalTitle(
            originalTitle = request.title,
            selectedAudioTrackLabel = request.selectedAudioTrackLabel,
            translatedTitle = translatedTitle
        )

        return MediaConverter.convertVideoToMp4(
            context = context,
            videoInputFile = videoTempFile,
            audioInputFile = audioTempFile,
            desiredTitle = finalTitle,
            requestedHeight = targetHeight,
            requestedAudioBitrateKbps = selectedAudio?.let { YoutubeFormatsService.audioKbps(it) } ?: 192
        )
    }

    private fun getContentLengthCompat(connection: HttpURLConnection): Long {
        return if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.N) {
            connection.contentLengthLong
        } else {
            connection.contentLength.toLong()
        }.takeIf { it > 0L } ?: -1L
    }

    private fun downloadStreamToFile(
        context: Context,
        notificationId: Int,
        fileTitle: String,
        stream: Stream,
        file: File,
        isAudio: Boolean,
        stageText: String,
        tracker: CombinedDownloadTracker,
        phase: DownloadPhase,
        extraLabel: String? = null
    ) {
        val content = stream.content ?: throw IllegalStateException("Нет URL для загрузки")

        val connection = (URL(content).openConnection() as HttpURLConnection).apply {
            requestMethod = "GET"
            connectTimeout = 30000
            readTimeout = 30000
            instanceFollowRedirects = true
            
            // Идентичные заголовки для DASH-фрагментов
            setRequestProperty("User-Agent", USER_AGENT)
            setRequestProperty("Accept", "*/*")
            setRequestProperty("Accept-Language", "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7")
            setRequestProperty("Connection", "keep-alive")
            
            if (content.contains("googlevideo.com")) {
                setRequestProperty("Referer", "https://www.youtube.com/")
                setRequestProperty("Origin", "https://www.youtube.com")
                setRequestProperty("Sec-Fetch-Mode", "cors")
                setRequestProperty("Sec-Fetch-Site", "cross-site")
                setRequestProperty("Sec-Fetch-Dest", "empty")
            }
        }

        val total = getContentLengthCompat(connection)
        val startedAt = System.currentTimeMillis()
        var lastBucket = -1

        connection.inputStream.use { input ->
            FileOutputStream(file).use { output ->
                val buffer = ByteArray(16384 * 2)
                var read: Int
                var written = 0L

                while (input.read(buffer).also { read = it } != -1) {
                    output.write(buffer, 0, read)
                    written += read

                    when (phase) {
                        DownloadPhase.VIDEO -> tracker.downloadedVideoBytes = written
                        DownloadPhase.AUDIO -> tracker.downloadedAudioBytes = written
                    }

                    val percent = tracker.combinedPercent()
                    val bucket = (percent * 5).roundToInt()

                    if (bucket != lastBucket) {
                        lastBucket = bucket

                        val now = System.currentTimeMillis()
                        val elapsedMillis = (now - startedAt).coerceAtLeast(1L)
                        val speedBytesPerMs = written.toDouble() / elapsedMillis
                        val speedBytesPerSec = (speedBytesPerMs * 1000).toLong()

                        val etaStr = if (total > 0 && speedBytesPerMs > 0) {
                            val remainingBytes = total - written
                            val remainingMillis = (remainingBytes / speedBytesPerMs).toLong()
                            formatEta(remainingMillis / 1000)
                        } else "—"

                        val sizeInfo = buildString {
                            append(stageText)
                            if (!extraLabel.isNullOrBlank()) append(" [$extraLabel]")
                            append(" • ")
                            append(formatBytes(written))
                            if (total > 0) append(" / ${formatBytes(total)}")
                        }

                        DownloadNotificationHelper.updateDownloadProgress(
                            context = context,
                            notificationId = notificationId,
                            fileName = fileTitle,
                            isAudio = isAudio,
                            percent = percent,
                            speed = formatSpeed(speedBytesPerSec),
                            eta = etaStr,
                            sizeInfo = sizeInfo
                        )
                    }
                }
            }
        }

        connection.disconnect()
    }

    private fun probeContentLength(contentUrl: String?): Long {
        if (contentUrl.isNullOrBlank()) return -1L

        return try {
            val connection = (URL(contentUrl).openConnection() as HttpURLConnection).apply {
                requestMethod = "GET"
                setRequestProperty("Range", "bytes=0-1")
                connectTimeout = 10000
                readTimeout = 10000
                instanceFollowRedirects = true
                setRequestProperty("User-Agent", USER_AGENT)
                
                if (contentUrl.contains("googlevideo.com")) {
                    setRequestProperty("Referer", "https://www.youtube.com/")
                    setRequestProperty("Origin", "https://www.youtube.com")
                    setRequestProperty("Sec-Fetch-Mode", "cors")
                    setRequestProperty("Sec-Fetch-Site", "cross-site")
                }
            }
            
            val rangeHeader = connection.getHeaderField("Content-Range")
            val len = if (rangeHeader != null) {
                rangeHeader.substringAfterLast("/").toLongOrNull() ?: -1L
            } else {
                getContentLengthCompat(connection)
            }
            
            connection.disconnect()
            if (len > 0) len else -1L
        } catch (_: Exception) {
            -1L
        }
    }

    private enum class DownloadPhase {
        VIDEO, AUDIO
    }

    private class CombinedDownloadTracker(
        val totalVideoBytes: Long,
        val totalAudioBytes: Long
    ) {
        var downloadedVideoBytes: Long = 0L
        var downloadedAudioBytes: Long = 0L

        fun combinedPercent(): Double {
            val knownVideo = totalVideoBytes > 0
            val knownAudio = totalAudioBytes > 0

            return when {
                knownVideo && knownAudio -> {
                    val total = (totalVideoBytes + totalAudioBytes).coerceAtLeast(1L)
                    val done = (downloadedVideoBytes + downloadedAudioBytes).coerceAtMost(total)
                    (done.toDouble() * 100.0 / total.toDouble()).coerceIn(0.0, 100.0)
                }

                knownVideo && !knownAudio -> {
                    val videoPart = (downloadedVideoBytes.toDouble() * 85.0 / totalVideoBytes.toDouble()).coerceIn(0.0, 85.0)
                    val audioPart = if (downloadedAudioBytes > 0L) 10.0 else 0.0
                    (videoPart + audioPart).coerceIn(0.0, 95.0)
                }

                !knownVideo && knownAudio -> {
                    val audioPart = (downloadedAudioBytes.toDouble() * 100.0 / totalAudioBytes.toDouble()).coerceIn(0.0, 100.0)
                    audioPart
                }

                else -> {
                    when {
                        downloadedAudioBytes > 0L -> 90.0
                        downloadedVideoBytes > 0L -> 50.0
                        else -> 0.0
                    }
                }
            }
        }
    }

    private fun parseBitrateKbps(label: String?): Int? {
        if (label.isNullOrBlank()) return null
        val m = Regex("""(\d+)""").find(label)
        return m?.groupValues?.getOrNull(1)?.toIntOrNull()
    }

    private fun audioExtension(stream: AudioStream): String {
        return stream.format?.suffix?.lowercase(Locale.ROOT)?.ifBlank { null } ?: "bin"
    }

    private fun videoExtension(stream: VideoStream): String {
        return stream.format?.suffix?.lowercase(Locale.ROOT)?.ifBlank { null } ?: "bin"
    }

    private fun uniqueTargetFile(dir: File, title: String, ext: String): File {
        val safeBase = sanitizeFileName(title).ifBlank { "Media" }
        var file = File(dir, "$safeBase.$ext")
        var counter = 2
        while (file.exists()) {
            file = File(dir, "$safeBase ($counter).$ext")
            counter++
        }
        return file
    }

    private fun sanitizeFileName(name: String): String {
        return name.replace(Regex("""[\\/:*?\"<>|]"""), "").replace(Regex("""\s+"""), " ").trim()
    }

    private fun formatBytes(bytes: Long): String {
        if (bytes < 1024) return "$bytes B"
        val kb = bytes / 1024.0
        if (kb < 1024) return String.format(Locale.US, "%.1f KB", kb)
        val mb = kb / 1024.0
        if (mb < 1024) return String.format(Locale.US, "%.1f MB", mb)
        val gb = mb / 1024.0
        return String.format(Locale.US, "%.2f GB", gb)
    }

    private fun formatEta(seconds: Long): String {
        if (seconds <= 0) return "—"
        if (seconds < 60) return "${seconds}s"
        val mins = seconds / 60
        val secs = seconds % 60
        return String.format(Locale.US, "%dm %ds", mins, secs)
    }

    private fun formatSpeed(bytesPerSecond: Long): String {
        return "${formatBytes(bytesPerSecond)}/s"
    }
}
