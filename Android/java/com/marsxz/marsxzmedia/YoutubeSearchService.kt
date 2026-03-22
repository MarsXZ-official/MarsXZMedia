package com.marsxz.marsxzmedia

import org.json.JSONObject
import java.io.BufferedReader
import java.io.InputStreamReader
import java.net.HttpURLConnection
import java.net.URL

object YoutubeSearchService {
    private const val API_KEY = "PUT_YOUR_YOUTUBE_DATA_API_KEY_HERE"
    private const val ANDROID_PACKAGE = "com.marsxz.marsxzmedia"
    private const val ANDROID_CERT_SHA1 = "PUT_YOUR_DATA_CERT_SHA1_HERE"

    fun searchByUrl(inputUrl: String): Result<VideoInfo> {
        if (API_KEY.isBlank() || API_KEY == "PUT_YOUR_YOUTUBE_DATA_API_KEY_HERE") {
            return Result.failure(IllegalStateException("Не задан YouTube API key"))
        }

        if (ANDROID_CERT_SHA1.isBlank() || ANDROID_CERT_SHA1 == "PUT_YOUR_ANDROID_CERT_SHA1_HERE") {
            return Result.failure(IllegalStateException("Не задан SHA1-хеш для Android-сертификата"))
        }

        val videoId = YoutubeUrlParser.extractVideoId(inputUrl)
            ?: return Result.failure(IllegalArgumentException("Не удалось распознать YouTube ссылку"))

        return try {
            val apiUrl = buildString {
                append("https://www.googleapis.com/youtube/v3/videos")
                append("?part=snippet,contentDetails")
                append("&id=").append(videoId)
                append("&key=").append(API_KEY)
            }

            val jsonText = httpGet(apiUrl)
            val root = JSONObject(jsonText)
            val items = root.optJSONArray("items")
                ?: return Result.failure(IllegalStateException("Видео не найдено"))

            if (items.length() == 0) {
                return Result.failure(IllegalStateException("Видео не найдено"))
            }

            val item = items.getJSONObject(0)
            val snippet = item.getJSONObject("snippet")
            val contentDetails = item.getJSONObject("contentDetails")
            val thumbnails = snippet.optJSONObject("thumbnails")

            val info = VideoInfo(
                videoId = videoId,
                title = snippet.optString("title", "Без названия"),
                description = snippet.optString("description", "Описание отсутствует"),
                author = snippet.optString("channelTitle", "Неизвестный автор"),
                durationText = formatIso8601Duration(contentDetails.optString("duration", "PT0S")),
                thumbnailUrl = pickThumbnail(thumbnails)
            )

            Result.success(info)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    private fun httpGet(urlText: String): String {
        val url = URL(urlText)
        val conn = (url.openConnection() as HttpURLConnection).apply {
            requestMethod = "GET"
            connectTimeout = 15000
            readTimeout = 15000
            setRequestProperty("Accept", "application/json")
            setRequestProperty("X-Android-Package", ANDROID_PACKAGE)
            setRequestProperty("X-Android-Cert", ANDROID_CERT_SHA1)
        }

        try {
            val code = conn.responseCode
            val stream = if (code in 200..299) conn.inputStream else conn.errorStream
            val text = if (stream != null) {
                BufferedReader(InputStreamReader(stream, Charsets.UTF_8)).use { it.readText() }
            } else {
                ""
            }

            if (code !in 200..299) {
                throw IllegalStateException("HTTP $code: $text")
            }

            return text
        } finally {
            conn.disconnect()
        }
    }

    private fun pickThumbnail(thumbnails: JSONObject?): String? {
        if (thumbnails == null) return null
        for (key in listOf("maxres", "standard", "high", "medium", "default")) {
            val url = thumbnails.optJSONObject(key)?.optString("url")
            if (!url.isNullOrBlank()) return url
        }
        return null
    }

    private fun formatIso8601Duration(iso: String): String {
        val match = Regex("PT(?:(\\d+)H)?(?:(\\d+)M)?(?:(\\d+)S)?").matchEntire(iso) ?: return "--:--"
        val hours = match.groupValues[1].toIntOrNull() ?: 0
        val minutes = match.groupValues[2].toIntOrNull() ?: 0
        val seconds = match.groupValues[3].toIntOrNull() ?: 0
        return if (hours > 0) {
            "%d:%02d:%02d".format(hours, minutes, seconds)
        } else {
            "%02d:%02d".format(minutes, seconds)
        }
    }
}
