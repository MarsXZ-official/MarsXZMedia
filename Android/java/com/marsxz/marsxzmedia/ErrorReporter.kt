package com.marsxz.marsxzmedia

import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Build
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat

object ErrorReporter {
    private const val SUPPORT_EMAIL = "marsmanecraft@gmail.com"
    private const val CHANNEL_ID = "marsxz_media_events_v15"

    /**
     * Показывает красивое уведомление об ошибке.
     * @param code Код ошибки (например, 404, 403)
     * @param rawError Полный текст ошибки для логов (будет замаскирован в письме)
     * @param tag Тег для внутреннего лога
     * @param isAudio Флаг, была ли это попытка загрузки аудио (для текста 404)
     */
    fun report(context: Context, code: String, rawError: String, tag: String = "Main", isAudio: Boolean? = null) {
        // 1. Логируем полную ошибку (скрыто от пользователя) сразу в два лога (Logcat + Файл)
        AppLog.write(context, "E", "[$tag] Код: $code | Ошибка: $rawError", tag)

        // 2. Формируем заголовок и сообщение для уведомления
        val userTitle = "$code: " + when {
            isAudio == true -> "Аудио не найдено"
            isAudio == false -> "Видео не найдено"
            else -> "Ошибка запроса"
        }

        val userMessage = when (code) {
            "403" -> "Доступ заблокирован YouTube. Сообщите MarsXZ."
            "404" -> "Ресурс отсутствует или удален."
            else -> "Произошла неизвестная проблема."
        }
        val fullDescription = "$userMessage\nОбратитесь за помощью к Создателю (MarsXZ) для решения."

        // 3. Подготавливаем письмо с логами
        val emailIntent = createEmailIntent(code, rawError)
        val pendingIntent = PendingIntent.getActivity(
            context, code.hashCode(), emailIntent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )

        // 4. Показываем уведомление (без палева технических данных)
        val n = NotificationCompat.Builder(context, CHANNEL_ID)
            .setSmallIcon(android.R.drawable.stat_notify_error)
            .setContentTitle(userTitle)
            .setContentText(userMessage)
            .setStyle(NotificationCompat.BigTextStyle().bigText(fullDescription))
            .setContentIntent(pendingIntent)
            .setAutoCancel(true)
            .setPriority(NotificationCompat.PRIORITY_HIGH)
            .build()

        try {
            // Проверка разрешений для Android 13+
            if (Build.VERSION.SDK_INT < 33 || 
                context.checkSelfPermission(android.Manifest.permission.POST_NOTIFICATIONS) == android.content.pm.PackageManager.PERMISSION_GRANTED) {
                NotificationManagerCompat.from(context).notify(code.hashCode(), n)
            }
        } catch (_: Exception) {}
    }

    private fun createEmailIntent(code: String, rawError: String): Intent {
        val device = "${Build.MANUFACTURER} ${Build.MODEL} (Android ${Build.VERSION.RELEASE}, API ${Build.VERSION.SDK_INT})"
        val censoredError = redactSensitiveInfo(rawError)
        
        val subject = "MarsXZ Media: Ошибка [$code]"
        val body = """
            Здравствуйте, у меня возникла проблема в приложении MarsXZ Media.
            
            --- Информация о системе ---
            Устройство: $device
            Код ошибки: $code
            
            --- Лог ошибки ---
            $censoredError
            
            ----------------------------
        """.trimIndent()

        val uri = Uri.parse("mailto:$SUPPORT_EMAIL")
            .buildUpon()
            .appendQueryParameter("subject", subject)
            .appendQueryParameter("body", body)
            .build()

        return Intent(Intent.ACTION_SENDTO, uri)
    }

    private fun redactSensitiveInfo(text: String): String {
        var result = text
        
        // Маскируем API Key (Google Style)
        val apiKeyRegex = Regex("AIza[0-9A-Za-z-_]{35}")
        result = result.replace(apiKeyRegex, "[секретный ключ API]")

        // Маскируем SHA-1 (40 hex chars)
        val sha1Regex = Regex("([0-9A-F]{2}:){19}[0-9A-F]{2}", RegexOption.IGNORE_CASE)
        result = result.replace(sha1Regex, "[SHA-1 отпечаток]")

        return result
    }
}
