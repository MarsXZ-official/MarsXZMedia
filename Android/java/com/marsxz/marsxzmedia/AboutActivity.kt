package com.marsxz.marsxzmedia

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.view.WindowManager
import android.widget.ImageButton
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.core.view.WindowInsetsControllerCompat

class AboutActivity : AppCompatActivity() {

    companion object {
        private const val SUPPORT_EMAIL = "marsxz8656@gmail.com"
    }

    private lateinit var backButton: ImageButton
    private lateinit var tvSupportEmail: TextView

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        // Внутри onCreate, перед setContentView
        window.setFlags(
            WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS,
            WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS
        )

// Чтобы иконки (время, батарея) были темными на белом фоне:
        WindowInsetsControllerCompat(window, window.decorView).apply {
            isAppearanceLightStatusBars = true // Темные иконки статус-бара
            isAppearanceLightNavigationBars = true // Темные иконки навигации
        }

        setContentView(R.layout.activity_about)
        UiSoundPlayer.init(this)

        // Инициализация View (ID из нашего нового XML)
        backButton = findViewById(R.id.backButton)
        tvSupportEmail = findViewById(R.id.tvSupportEmail)
        // Убедитесь, что используете ID из XML: tvSupportEmail
        val tvSupportEmail = findViewById<TextView>(R.id.tvSupportEmail)

        // ДОБАВЛЯЕМ ПОДЧЕРКИВАНИЕ ПРОГРАММНО
        tvSupportEmail.paintFlags = tvSupportEmail.paintFlags or android.graphics.Paint.UNDERLINE_TEXT_FLAG

        backButton.setOnClickListener {
            UiSoundPlayer.playClick()
            finish()
        }

        tvSupportEmail.text = SUPPORT_EMAIL
        tvSupportEmail.setOnClickListener {
            UiSoundPlayer.playClick() // ДОБАВИТЬ ЗВУК
            sendEmail()
        }

        // Если вы хотите программно менять версию в заголовке,
        // убедитесь, что в XML у TextView с версией есть ID, например android:id="@+id/tvVersion"
        // findViewById<TextView>(R.id.tvVersion)?.text = "MarsXZ Media v${BuildConfig.VERSION_NAME}"
    }

    private fun sendEmail() {
        // 1. Попытка открыть через нативное приложение (mailto:)
        val mailtoIntent = Intent(Intent.ACTION_SENDTO).apply {
            data = Uri.parse("mailto:$SUPPORT_EMAIL")
            putExtra(Intent.EXTRA_SUBJECT, "MarsXZ Media Feedback (Android)")
        }

        try {
            startActivity(Intent.createChooser(mailtoIntent, "Отправить через..."))
        } catch (e: Exception) {
            // 2. Если почтовых приложений нет, пробуем открыть Gmail в браузере
            val gmailBrowserUri = Uri.parse(
                "https://mail.google.com/mail/?view=cm&fs=1&to=${Uri.encode(SUPPORT_EMAIL)}&su=${Uri.encode("MarsXZ Media Feedback")}"
            )
            val browserIntent = Intent(Intent.ACTION_VIEW, gmailBrowserUri)

            try {
                startActivity(browserIntent)
                Toast.makeText(this, "Открываю Gmail в браузере...", Toast.LENGTH_SHORT).show()
            } catch (e2: Exception) {
                // 3. Крайний случай: копируем в буфер
                copyToClipboard(SUPPORT_EMAIL)
                Toast.makeText(this, "Почта скопирована в буфер обмена", Toast.LENGTH_LONG).show()
            }
        }
    }

    private fun copyToClipboard(text: String) {
        val clipboard = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
        val clip = ClipData.newPlainText("MarsXZ Support", text)
        clipboard.setPrimaryClip(clip)
    }
}