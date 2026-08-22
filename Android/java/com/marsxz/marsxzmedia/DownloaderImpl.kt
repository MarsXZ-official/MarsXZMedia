package com.marsxz.marsxzmedia

import org.schabi.newpipe.extractor.downloader.Downloader
import org.schabi.newpipe.extractor.downloader.Request
import org.schabi.newpipe.extractor.downloader.Response
import java.io.BufferedReader
import java.io.InputStream
import java.io.InputStreamReader
import java.net.HttpURLConnection
import java.net.URL
import java.util.zip.GZIPInputStream
import java.util.zip.InflaterInputStream

/**
 * Реализация загрузчика для NewPipeExtractor.
 * Исправляет проблему "только 360p", заставляя YouTube отдавать DASH-потоки высокого качества.
 */
class DownloaderImpl : Downloader() {
    
    companion object {
        // Современный User-Agent (Chrome 131)
        const val DESKTOP_UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36"
    }

    private val cookieMap = mutableMapOf<String, String>().apply {
        put("CONSENT", "YES+cb.20220301-11-p0.en+FX+700")
        put("PREF", "hl=ru&tz=Europe.Moscow")
    }

    override fun execute(request: Request): Response {
        val url = request.url()
        val connection = (URL(url).openConnection() as HttpURLConnection).apply {
            requestMethod = request.httpMethod()
            instanceFollowRedirects = true
            connectTimeout = 30000
            readTimeout = 30000
            
            // Базовые заголовки браузера
            setRequestProperty("User-Agent", DESKTOP_UA)
            setRequestProperty("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8")
            setRequestProperty("Accept-Language", "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7")
            setRequestProperty("Accept-Encoding", "gzip, deflate")
            
            if (url.contains("youtube.com") || url.contains("googlevideo.com")) {
                setRequestProperty("Referer", "https://www.youtube.com/")
                setRequestProperty("Origin", "https://www.youtube.com")
                
                // Client Hints — помогают YouTube "узнать" современный Chrome
                setRequestProperty("Sec-CH-UA", "\"Google Chrome\";v=\"131\", \"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"")
                setRequestProperty("Sec-CH-UA-Mobile", "?0")
                setRequestProperty("Sec-CH-UA-Platform", "\"Windows\"")
                setRequestProperty("Sec-CH-UA-Platform-Version", "\"15.0.0\"")
                setRequestProperty("Sec-CH-UA-Full-Version-List", "\"Google Chrome\";v=\"131.0.6778.205\", \"Chromium\";v=\"131.0.6778.205\", \"Not_A Brand\";v=\"24.0.0.0\"")

                // Sec-Fetch заголовки для DASH-потоков
                if (url.contains("/api/") || url.contains("googlevideo.com") || url.contains("/v1/")) {
                    setRequestProperty("Sec-Fetch-Dest", "empty")
                    setRequestProperty("Sec-Fetch-Mode", "cors")
                    setRequestProperty("Sec-Fetch-Site", if (url.contains("googlevideo.com")) "cross-site" else "same-origin")
                }
                
                // Идентификатор посетителя — КРИТИЧНО для DASH (1080p+)
                setRequestProperty("X-Goog-Visitor-Id", "CgtBcVZpU0Z6S0RReE5pdyi_p6u0BjIKCgJSVRICR0I%3D")
                setRequestProperty("X-Youtube-Client-Name", "1")
                // Обновляем версию клиента до самой актуальной
                setRequestProperty("X-Youtube-Client-Version", "2.20260301.01.00")
                setRequestProperty("X-Youtube-Page-CL", "604033333")
                setRequestProperty("X-Youtube-Page-Label", "youtube.ytfe.desktop_20260301_01_RC00")
                setRequestProperty("X-Youtube-Page-Origin", "https://www.youtube.com")
            }
        }

        // Синхронизация куки
        val mergedCookies = cookieMap.toMutableMap()
        request.headers()["Cookie"]?.forEach { header ->
            header.split(";").forEach {
                val part = it.trim()
                val idx = part.indexOf('=')
                if (idx > 0) mergedCookies[part.substring(0, idx)] = part.substring(idx + 1)
            }
        }
        
        if (mergedCookies.isNotEmpty()) {
            connection.setRequestProperty("Cookie", mergedCookies.entries.joinToString("; ") { "${it.key}=${it.value}" })
        }

        // Переносим остальные заголовки из библиотеки. 
        // ВАЖНО: Мы НЕ даем библиотеке перетереть наш User-Agent, если она пытается подставить мобильный.
        request.headers().forEach { (name, values) ->
            if (!name.equals("Cookie", ignoreCase = true) && 
                !name.equals("Accept-Encoding", ignoreCase = true) &&
                !name.equals("User-Agent", ignoreCase = true)) {
                connection.setRequestProperty(name, values.joinToString(", "))
            }
        }

        if (request.httpMethod() == "POST") {
            request.dataToSend()?.let { data ->
                connection.doOutput = true
                connection.outputStream.use { os -> os.write(data) }
            }
        }

        val responseCode = connection.responseCode
        
        // Сбор новых куки
        connection.headerFields["Set-Cookie"]?.forEach { header ->
            val cookie = header.split(';')[0]
            val idx = cookie.indexOf('=')
            if (idx > 0) cookieMap[cookie.substring(0, idx).trim()] = cookie.substring(idx + 1).trim()
        }

        val encoding = connection.contentEncoding?.lowercase()
        val rawStream = if (responseCode in 200..299) connection.inputStream else connection.errorStream
        
        val bodyText = rawStream?.let { s ->
            val wrapped = when {
                encoding?.contains("gzip") == true -> GZIPInputStream(s)
                encoding?.contains("deflate") == true -> InflaterInputStream(s)
                else -> s
            }
            wrapped.bufferedReader(Charsets.UTF_8).use { it.readText() }
        } ?: ""

        val responseHeaders = connection.headerFields.filterKeys { it != null }.mapValues { it.value ?: emptyList() }
        val finalUrl = connection.url.toString()
        val msg = connection.responseMessage ?: ""
        connection.disconnect()

        return Response(responseCode, msg, responseHeaders, bodyText, finalUrl)
    }
}
