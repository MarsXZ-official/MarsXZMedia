package com.marsxz.marsxzmedia

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.widget.ImageButton
import android.widget.TextView
import android.widget.Toast
import androidx.activity.enableEdgeToEdge
import androidx.appcompat.app.AppCompatActivity
import androidx.core.view.ViewCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat

class AboutActivity : AppCompatActivity() {

    companion object {
        private const val SUPPORT_EMAIL = "marsmanecraft@gmail.com"
    }

    private lateinit var backButton: ImageButton
    private lateinit var tvSupportEmail: TextView
    private lateinit var tvSupportYoutube: TextView
    private lateinit var tvSupportGithub: TextView
    
    private var currentAppliedFont: String? = null
    private var currentAppliedSquare: Boolean = false

    override fun onCreate(savedInstanceState: Bundle?) {
        // ПРИМЕНЯЕМ ТЕМУ СО ШРИФТОМ
        val prefs = getSharedPreferences("app_settings", Context.MODE_PRIVATE)
        val fontType = prefs.getString("font_type", "system")
        val isSquare = prefs.getBoolean("minecraft_ui", false)
        
        currentAppliedFont = fontType
        currentAppliedSquare = isSquare

        when {
            isSquare && fontType == "monocraft" -> setTheme(R.style.Theme_MarsXZMedia_Square_Monocraft)
            isSquare -> setTheme(R.style.Theme_MarsXZMedia_Square)
            fontType == "monocraft" -> setTheme(R.style.Theme_MarsXZMedia_Monocraft)
            else -> setTheme(R.style.Theme_MarsXZMedia)
        }

        enableEdgeToEdge()
        super.onCreate(savedInstanceState)

        setContentView(R.layout.activity_about)

        // Force system bar icons color based on theme
        val isNightMode = (resources.configuration.uiMode and android.content.res.Configuration.UI_MODE_NIGHT_MASK) == android.content.res.Configuration.UI_MODE_NIGHT_YES
        WindowInsetsControllerCompat(window, window.decorView).apply {
            isAppearanceLightStatusBars = !isNightMode
            isAppearanceLightNavigationBars = !isNightMode
        }

        ViewCompat.setOnApplyWindowInsetsListener(findViewById(R.id.topBar)) { v, insets ->
            val bars = insets.getInsets(WindowInsetsCompat.Type.systemBars())
            v.setPadding(v.paddingLeft, bars.top, v.paddingRight, v.paddingBottom)
            insets
        }
        UiSoundPlayer.init(this)

        backButton = findViewById(R.id.backButton)
        tvSupportEmail = findViewById(R.id.tvSupportEmail)
        tvSupportYoutube = findViewById(R.id.tvSupportYoutube)
        tvSupportGithub = findViewById(R.id.tvSupportGithub)

        tvSupportEmail.paintFlags = tvSupportEmail.paintFlags or android.graphics.Paint.UNDERLINE_TEXT_FLAG

        backButton.setOnClickListener {
            UiSoundPlayer.playClick(this)
            finish()
        }

        tvSupportEmail.text = SUPPORT_EMAIL
        tvSupportEmail.setOnClickListener {
            UiSoundPlayer.playClick(this)
            sendEmail()
        }

        tvSupportYoutube.setOnClickListener {
            UiSoundPlayer.playClick(this)
            openUrl("https://m.youtube.com/@MarsXZ")
        }

        tvSupportGithub.setOnClickListener {
            UiSoundPlayer.playClick(this)
            openUrl("https://github.com/MarsXZ-Official/MarsXZMedia")
        }
    }

    private fun openUrl(url: String) {
        try {
            startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url)))
        } catch (e: Exception) {
            Toast.makeText(this, "Не удалось открыть ссылку", Toast.LENGTH_SHORT).show()
        }
    }

    override fun onResume() {
        super.onResume()
        val prefs = getSharedPreferences("app_settings", Context.MODE_PRIVATE)
        val fontType = prefs.getString("font_type", "system")
        val isSquare = prefs.getBoolean("minecraft_ui", false)
        
        if (fontType != currentAppliedFont || isSquare != currentAppliedSquare) {
            recreate()
        }
    }

    private fun sendEmail() {
        val mailtoIntent = Intent(Intent.ACTION_SENDTO).apply {
            data = Uri.parse("mailto:$SUPPORT_EMAIL")
            putExtra(Intent.EXTRA_SUBJECT, "MarsXZ Media Feedback (Android)")
        }

        try {
            startActivity(Intent.createChooser(mailtoIntent, "Отправить через..."))
        } catch (e: Exception) {
            val gmailBrowserUri = Uri.parse(
                "https://mail.google.com/mail/?view=cm&fs=1&to=${Uri.encode(SUPPORT_EMAIL)}&su=${Uri.encode("MarsXZ Media Feedback")}"
            )
            val browserIntent = Intent(Intent.ACTION_VIEW, gmailBrowserUri)

            try {
                startActivity(browserIntent)
                Toast.makeText(this, "Открываю Gmail в браузере...", Toast.LENGTH_SHORT).show()
            } catch (e2: Exception) {
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
