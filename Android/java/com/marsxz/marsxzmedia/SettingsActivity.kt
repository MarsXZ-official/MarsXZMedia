package com.marsxz.marsxzmedia

import android.Manifest
import android.content.ActivityNotFoundException
import android.content.Context
import android.content.Intent
import android.content.SharedPreferences
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.Environment
import android.provider.DocumentsContract
import android.provider.Settings
import android.view.View
import android.widget.*
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AlertDialog
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import androidx.core.view.ViewCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import java.io.File
import java.io.FileInputStream

class SettingsActivity : AppCompatActivity() {

    companion object {
        private const val ROOT_PATH = "/storage/emulated/0/"
    }

    private var initialSnapshot: SettingsSnapshot? = null
    private var changeMessageShown = false
    private var requestedManageStorage = false

    private data class SettingsSnapshot(
        val fontType: String,
        val soundTheme: String,
        val minecraftUI: Boolean,
        val useDefaultPath: Boolean,
        val separatePaths: Boolean,
        val noSubfolders: Boolean,
        val dontOpenFile: Boolean,
        val disableLogs: Boolean,
        val infiniteLogs: Boolean,
        val maxLogDays: Int,
        val videoPath: String?,
        val musicPath: String?
    )

    private lateinit var prefs: SharedPreferences
    private var isUpdatingUI = false
    private var currentAppliedFont: String? = null
    private var currentAppliedSquare: Boolean = false

    private lateinit var backButton: ImageButton

    private lateinit var cbFontMonoCraft: CheckBox
    private lateinit var cbSoundsMinecraft: CheckBox
    private lateinit var cbMinecraftUI: CheckBox

    private lateinit var tvVideoPathLabel: TextView
    private lateinit var etVideoPath: EditText
    private lateinit var btnSelectVideoPath: Button
    private lateinit var layoutMusicPath: LinearLayout
    private lateinit var etMusicPath: EditText
    private lateinit var btnSelectMusicPath: Button

    private lateinit var cbSeparatePaths: CheckBox
    private lateinit var cbNoSubfolders: CheckBox
    private lateinit var cbDontOpenFile: CheckBox
    private lateinit var cbUseDefaultPath: CheckBox

    private lateinit var btnExportLogs: Button
    private lateinit var cbDisableLogs: CheckBox
    private lateinit var etMaxDays: EditText
    private lateinit var cbInfiniteLogs: CheckBox
    private lateinit var tvLogInfo: TextView
    private lateinit var tvErrorInfo: TextView

    private var pendingExportLogFile: File? = null

    private val requestWriteStorageLauncher =
        registerForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
            if (granted) {
                prefs.edit().putBoolean("use_default_path", false).apply()
                Toast.makeText(this, "Разрешение предоставлено", Toast.LENGTH_SHORT).show()
            } else {
                revertToDefaultPath("Разрешение не предоставлено")
            }
            updateUI()
        }

    private val selectVideoDirLauncher =
        registerForActivityResult(ActivityResultContracts.OpenDocumentTree()) { uri ->
            uri?.let { savePickedFolder(it, isMusic = false) }
        }

    private val selectMusicDirLauncher =
        registerForActivityResult(ActivityResultContracts.OpenDocumentTree()) { uri ->
            uri?.let { savePickedFolder(it, isMusic = true) }
        }

    private val exportLogLauncher =
        registerForActivityResult(ActivityResultContracts.CreateDocument("application/octet-stream")) { uri: Uri? ->
            if (uri == null) {
                tvErrorInfo.visibility = View.VISIBLE
                tvErrorInfo.text = "Экспорт отменён"
                tvErrorInfo.setTextColor(android.graphics.Color.GRAY)
                pendingExportLogFile = null
                return@registerForActivityResult
            }

            val source = pendingExportLogFile
            if (source == null || !source.exists()) {
                tvErrorInfo.visibility = View.VISIBLE
                tvErrorInfo.text = "Файл combined_app.log не найден"
                tvErrorInfo.setTextColor(android.graphics.Color.RED)
                pendingExportLogFile = null
                return@registerForActivityResult
            }

            try {
                contentResolver.openOutputStream(uri)?.use { out ->
                    FileInputStream(source).use { input ->
                        input.copyTo(out)
                    }
                } ?: throw IllegalStateException("Не удалось открыть файл назначения")

                tvErrorInfo.visibility = View.VISIBLE
                tvErrorInfo.text = "Логи экспортированы"
                tvErrorInfo.setTextColor(android.graphics.Color.parseColor("#2E7D32"))
            } catch (e: Exception) {
                tvErrorInfo.visibility = View.VISIBLE
                tvErrorInfo.text = "Ошибка экспорта: ${e.message}"
                tvErrorInfo.setTextColor(android.graphics.Color.RED)
            } finally {
                pendingExportLogFile = null
            }
        }

    override fun onCreate(savedInstanceState: Bundle?) {
        prefs = getSharedPreferences("app_settings", Context.MODE_PRIVATE)
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
        setContentView(R.layout.activity_settings)

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

        ViewCompat.setOnApplyWindowInsetsListener(findViewById(R.id.settingsScroll)) { v, insets ->
            val bars = insets.getInsets(WindowInsetsCompat.Type.systemBars())
            v.setPadding(v.paddingLeft, v.paddingTop, v.paddingRight, bars.bottom)
            insets
        }

        UiSoundPlayer.init(this)

        initViews()
        ensureDefaultSettings()
        loadSettings()
        setupListeners()
        updateUI()
        LogMaintenance.enforcePolicy(this)

        initialSnapshot = buildCurrentSnapshot()
    }

    override fun onResume() {
        super.onResume()
        changeMessageShown = false
        initialSnapshot = buildCurrentSnapshot()

        if (requestedManageStorage) {
            requestedManageStorage = false

            if (hasCustomPathAccess()) {
                prefs.edit().putBoolean("use_default_path", false).apply()

                isUpdatingUI = true
                cbUseDefaultPath.isChecked = false
                isUpdatingUI = false

                Toast.makeText(this, "Разрешение предоставлено", Toast.LENGTH_SHORT).show()
                updateUI()
            } else {
                revertToDefaultPath("Разрешение не предоставлено. Используется путь по умолчанию")
            }
        }
    }

    override fun onBackPressed() {
        // Выполняем финальное сохранение с уведомлением только при выходе
        performFullSave()
        super.onBackPressed()
    }

    override fun onPause() {
        super.onPause()
        // Блокируем уведомление, если это просто перезагрузка экрана для шрифта (isChangingConfigurations)
        if (!isChangingConfigurations) {
            performFullSave()
        }
    }

    private fun initViews() {
        backButton = findViewById(R.id.backButton)

        cbFontMonoCraft = findViewById(R.id.cbFontMonoCraft)
        cbSoundsMinecraft = findViewById(R.id.cbSoundsMinecraft)
        cbMinecraftUI = findViewById(R.id.cbMinecraftUI)

        tvVideoPathLabel = findViewById(R.id.tvVideoPathLabel)
        etVideoPath = findViewById(R.id.tvVideoPath)
        btnSelectVideoPath = findViewById(R.id.btnSelectVideoPath)
        layoutMusicPath = findViewById(R.id.layoutMusicPath)
        etMusicPath = findViewById(R.id.tvMusicPath)
        btnSelectMusicPath = findViewById(R.id.btnSelectMusicPath)

        cbSeparatePaths = findViewById(R.id.cbSeparatePaths)
        cbNoSubfolders = findViewById(R.id.cbNoSubfolders)
        cbDontOpenFile = findViewById(R.id.cbDontOpenFile)
        cbUseDefaultPath = findViewById(R.id.cbUseDefaultPath)

        btnExportLogs = findViewById(R.id.btnExportLogs)
        cbDisableLogs = findViewById(R.id.cbDisableLogs)
        etMaxDays = findViewById(R.id.etMaxDays)
        cbInfiniteLogs = findViewById(R.id.cbInfiniteLogs)
        tvLogInfo = findViewById(R.id.tvLogInfo)
        tvErrorInfo = findViewById(R.id.tvErrorInfo)
    }

    private fun ensureDefaultSettings() {
        val editor = prefs.edit()

        if (!prefs.contains("font_type")) editor.putString("font_type", "system")
        if (!prefs.contains("sound_theme")) editor.putString("sound_theme", "system")
        if (!prefs.contains("use_default_path")) editor.putBoolean("use_default_path", true)
        if (!prefs.contains("separate_paths")) editor.putBoolean("separate_paths", false)
        if (!prefs.contains("no_subfolders")) editor.putBoolean("no_subfolders", false)
        if (!prefs.contains("dont_open_file")) editor.putBoolean("dont_open_file", false)
        if (!prefs.contains("disable_logs")) editor.putBoolean("disable_logs", false)
        if (!prefs.contains("infinite_logs")) editor.putBoolean("infinite_logs", true)
        if (!prefs.contains("max_log_days")) editor.putInt("max_log_days", 365)

        if (!prefs.contains("video_path")) editor.putString("video_path", ROOT_PATH)
        if (!prefs.contains("music_path")) editor.putString("music_path", ROOT_PATH)
        if (!prefs.contains("video_path_last_valid")) editor.putString("video_path_last_valid", ROOT_PATH)
        if (!prefs.contains("music_path_last_valid")) editor.putString("music_path_last_valid", ROOT_PATH)

        editor.apply()
    }

    private fun loadSettings() {
        isUpdatingUI = true

        val fontType = prefs.getString("font_type", "system")
        cbFontMonoCraft.isChecked = fontType == "monocraft"

        val soundTheme = prefs.getString("sound_theme", "system")
        cbSoundsMinecraft.isChecked = soundTheme == "minecraft"

        cbMinecraftUI.isChecked = prefs.getBoolean("minecraft_ui", false)

        cbUseDefaultPath.isChecked = prefs.getBoolean("use_default_path", true)
        cbSeparatePaths.isChecked = prefs.getBoolean("separate_paths", false)
        cbNoSubfolders.isChecked = prefs.getBoolean("no_subfolders", false)
        cbDontOpenFile.isChecked = prefs.getBoolean("dont_open_file", false)

        cbDisableLogs.isChecked = prefs.getBoolean("disable_logs", false)
        cbInfiniteLogs.isChecked = prefs.getBoolean("infinite_logs", true)
        etMaxDays.setText(prefs.getInt("max_log_days", 365).toString())

        etVideoPath.setText(prefs.getString("video_path", ROOT_PATH) ?: ROOT_PATH)
        etMusicPath.setText(prefs.getString("music_path", ROOT_PATH) ?: ROOT_PATH)

        isUpdatingUI = false
    }

    private fun hasChanges(): Boolean {
        val snapshot = initialSnapshot ?: return false

        val currentFont = if (cbFontMonoCraft.isChecked) "monocraft" else "system"
        val currentSounds = if (cbSoundsMinecraft.isChecked) "minecraft" else "system"
        val currentSquare = cbMinecraftUI.isChecked
        val currentDays = etMaxDays.text.toString().toIntOrNull() ?: 365

        return currentFont != snapshot.fontType ||
                currentSounds != snapshot.soundTheme ||
                currentSquare != snapshot.minecraftUI ||
                cbUseDefaultPath.isChecked != snapshot.useDefaultPath ||
                cbSeparatePaths.isChecked != snapshot.separatePaths ||
                cbNoSubfolders.isChecked != snapshot.noSubfolders ||
                cbDontOpenFile.isChecked != snapshot.dontOpenFile ||
                cbDisableLogs.isChecked != snapshot.disableLogs ||
                cbInfiniteLogs.isChecked != snapshot.infiniteLogs ||
                currentDays != snapshot.maxLogDays ||
                (if (!cbUseDefaultPath.isChecked) normalizeUserPath(etVideoPath.text.toString()) != snapshot.videoPath else false) ||
                (if (!cbUseDefaultPath.isChecked && cbSeparatePaths.isChecked) normalizeUserPath(etMusicPath.text.toString()) != snapshot.musicPath else false)
    }

    private fun setupListeners() {
        backButton.setOnClickListener {
            UiSoundPlayer.playClick(this)
            performFullSave()
            finish()
        }

        cbMinecraftUI.setOnCheckedChangeListener { _, isChecked ->
            if (isUpdatingUI) return@setOnCheckedChangeListener
            UiSoundPlayer.playClick(this)
            prefs.edit()
                .putBoolean("minecraft_ui", isChecked)
                .putBoolean("pending_settings_notification", true)
                .commit()
            recreate()
        }

        // Мгновенное применение шрифта с перезагрузкой, но БЕЗ уведомления
        cbFontMonoCraft.setOnCheckedChangeListener { _, isChecked ->
            if (isUpdatingUI) return@setOnCheckedChangeListener
            UiSoundPlayer.playClick(this)

            val newFontType = if (isChecked) "monocraft" else "system"
            val currentFont = prefs.getString("font_type", "system")

            if (newFontType != currentFont) {
                // Ставим специальный флаг, чтобы вспомнить об изменении при выходе
                prefs.edit()
                    .putString("font_type", newFontType)
                    .putBoolean("pending_settings_notification", true)
                    .commit()
                recreate() // Мгновенно применяем шрифт
            }
        }

        // Мгновенное применение звуков
        cbSoundsMinecraft.setOnCheckedChangeListener { _, isChecked ->
            if (isUpdatingUI) return@setOnCheckedChangeListener
            UiSoundPlayer.playClick(this)

            val newTheme = if (isChecked) "minecraft" else "system"
            val currentTheme = prefs.getString("sound_theme", "system")

            if (newTheme != currentTheme) {
                // Ставим специальный флаг
                prefs.edit()
                    .putString("sound_theme", newTheme)
                    .putBoolean("pending_settings_notification", true)
                    .apply()
            }
        }

        cbUseDefaultPath.setOnCheckedChangeListener { _, isChecked ->
            if (isUpdatingUI) return@setOnCheckedChangeListener
            UiSoundPlayer.playClick(this)
            prefs.edit().putBoolean("use_default_path", isChecked).apply()

            if (isChecked) {
                updateUI()
            } else {
                ensureCustomPathDefaults()
                if (hasCustomPathAccess()) {
                    updateUI()
                } else {
                    requestCustomPathAccess()
                }
            }
        }

        cbSeparatePaths.setOnCheckedChangeListener { _, isChecked ->
            if (isUpdatingUI) return@setOnCheckedChangeListener
            UiSoundPlayer.playClick(this)
            prefs.edit().putBoolean("separate_paths", isChecked).apply()

            if (!isChecked) {
                val currentVideo = normalizeUserPath(etVideoPath.text?.toString())
                prefs.edit()
                    .putString("music_path", currentVideo)
                    .putString("music_path_last_valid", currentVideo)
                    .apply()
                etMusicPath.setText(currentVideo)
            }
            updateUI()
        }

        btnSelectVideoPath.setOnClickListener {
            UiSoundPlayer.playClick(this)
            selectVideoDirLauncher.launch(null)
        }

        btnSelectMusicPath.setOnClickListener {
            UiSoundPlayer.playClick(this)
            selectMusicDirLauncher.launch(null)
        }

        btnExportLogs.setOnClickListener {
            UiSoundPlayer.playClick(this)
            exportLogs()
        }

        cbNoSubfolders.setOnCheckedChangeListener { _, checked ->
            if (isUpdatingUI) return@setOnCheckedChangeListener
            UiSoundPlayer.playClick(this)
            prefs.edit().putBoolean("no_subfolders", checked).apply()
        }

        cbDontOpenFile.setOnCheckedChangeListener { _, checked ->
            if (isUpdatingUI) return@setOnCheckedChangeListener
            UiSoundPlayer.playClick(this)
            prefs.edit().putBoolean("dont_open_file", checked).apply()
        }

        cbDisableLogs.setOnCheckedChangeListener { _, checked ->
            if (isUpdatingUI) return@setOnCheckedChangeListener
            UiSoundPlayer.playClick(this)
            prefs.edit().putBoolean("disable_logs", checked).apply()
            LogMaintenance.enforcePolicy(this)
            updateUI()
        }

        cbInfiniteLogs.setOnCheckedChangeListener { _, checked ->
            if (isUpdatingUI) return@setOnCheckedChangeListener
            UiSoundPlayer.playClick(this)
            prefs.edit().putBoolean("infinite_logs", checked).apply()
            LogMaintenance.enforcePolicy(this)
            updateUI()
        }
    }

    private fun performFullSave() {
        val days = etMaxDays.text.toString().toIntOrNull() ?: 365

        val hasUnsavedChanges = hasChanges()
        val hasPendingNotification = prefs.getBoolean("pending_settings_notification", false)

        // Если есть изменения ИЛИ висит флаг от смены темы/звука
        if (hasUnsavedChanges || hasPendingNotification) {
            val editor = prefs.edit()

            editor.putString("font_type", if (cbFontMonoCraft.isChecked) "monocraft" else "system")
            editor.putString("sound_theme", if (cbSoundsMinecraft.isChecked) "minecraft" else "system")
            editor.putBoolean("minecraft_ui", cbMinecraftUI.isChecked)
            editor.putBoolean("use_default_path", cbUseDefaultPath.isChecked)
            editor.putBoolean("separate_paths", cbSeparatePaths.isChecked)
            editor.putBoolean("no_subfolders", cbNoSubfolders.isChecked)
            editor.putBoolean("dont_open_file", cbDontOpenFile.isChecked)
            editor.putBoolean("disable_logs", cbDisableLogs.isChecked)
            editor.putBoolean("infinite_logs", cbInfiniteLogs.isChecked)
            editor.putInt("max_log_days", days)

            if (!cbUseDefaultPath.isChecked) {
                val vPath = normalizeUserPath(etVideoPath.text.toString())
                editor.putString("video_path", vPath).putString("video_path_last_valid", vPath)
                if (cbSeparatePaths.isChecked) {
                    val mPath = normalizeUserPath(etMusicPath.text.toString())
                    editor.putString("music_path", mPath).putString("music_path_last_valid", mPath)
                } else {
                    editor.putString("music_path", vPath).putString("music_path_last_valid", vPath)
                }
            }

            // Очищаем флаг уведомления
            editor.putBoolean("pending_settings_notification", false)
            editor.apply()

            // Воспроизводим звук и показываем уведомление ТОЛЬКО ОДИН РАЗ при выходе
            if (!changeMessageShown) {
                changeMessageShown = true
                UiSoundPlayer.playApply(this)
                SettingsNotificationHelper.showSettingsSaved(this)
            }
            initialSnapshot = buildCurrentSnapshot()
        }
        LogMaintenance.enforcePolicy(this)
    }

    private fun hasCustomPathAccess(): Boolean {
        return when {
            Build.VERSION.SDK_INT >= Build.VERSION_CODES.R -> Environment.isExternalStorageManager()
            Build.VERSION.SDK_INT >= Build.VERSION_CODES.M -> ContextCompat.checkSelfPermission(this, Manifest.permission.WRITE_EXTERNAL_STORAGE) == PackageManager.PERMISSION_GRANTED
            else -> true
        }
    }

    private fun requestCustomPathAccess() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            requestedManageStorage = true
            try {
                startActivity(Intent(Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION).apply { data = Uri.parse("package:$packageName") })
            } catch (_: Exception) { revertToDefaultPath("Ошибка доступа") }
        } else if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            requestWriteStorageLauncher.launch(Manifest.permission.WRITE_EXTERNAL_STORAGE)
        }
    }

    private fun revertToDefaultPath(message: String) {
        prefs.edit().putBoolean("use_default_path", true).apply()
        isUpdatingUI = true
        cbUseDefaultPath.isChecked = true
        isUpdatingUI = false
        Toast.makeText(this, message, Toast.LENGTH_LONG).show()
        updateUI()
    }

    private fun updateUI() {
        val useDefault = cbUseDefaultPath.isChecked
        val separate = cbSeparatePaths.isChecked

        if (useDefault) {
            etVideoPath.setText("Используется путь по умолчанию")
            etMusicPath.setText("Используется путь по умолчанию")
            etVideoPath.isEnabled = false
            etMusicPath.isEnabled = false
            btnSelectVideoPath.isEnabled = false
            btnSelectMusicPath.isEnabled = false
            cbSeparatePaths.isEnabled = false
            if (separate) {
                isUpdatingUI = true
                cbSeparatePaths.isChecked = false
                prefs.edit().putBoolean("separate_paths", false).apply()
                isUpdatingUI = false
            }
        } else {
            ensureCustomPathDefaults()
            val videoPath = prefs.getString("video_path", ROOT_PATH) ?: ROOT_PATH
            val musicPath = prefs.getString("music_path", videoPath) ?: videoPath
            etVideoPath.setText(videoPath)
            etMusicPath.setText(musicPath)
            etVideoPath.isEnabled = true
            etMusicPath.isEnabled = separate
            btnSelectVideoPath.isEnabled = true
            cbSeparatePaths.isEnabled = true
            btnSelectMusicPath.isEnabled = separate
        }

        tvVideoPathLabel.text = if (separate && !useDefault) "Путь к Video:" else "Путь к Video и Audio:"
        layoutMusicPath.visibility = if (separate && !useDefault) View.VISIBLE else View.GONE

        if (cbDisableLogs.isChecked) {
            etMaxDays.isEnabled = false
            cbInfiniteLogs.isEnabled = false
            tvErrorInfo.visibility = View.VISIBLE
            tvErrorInfo.text = "Логи отключены."
            tvErrorInfo.setTextColor(android.graphics.Color.GRAY)
            tvLogInfo.visibility = View.GONE
        } else {
            cbInfiniteLogs.isEnabled = true
            etMaxDays.isEnabled = !cbInfiniteLogs.isChecked
            
            // Показываем подсказку о вечном хранилище только если включено безгранично
            tvLogInfo.visibility = if (cbInfiniteLogs.isChecked) View.VISIBLE else View.GONE
            
            // Убираем дубликат текста из tvErrorInfo
            if (tvErrorInfo.text == "Авто-удаление отключено." || tvErrorInfo.text == "Логи отключены.") {
                tvErrorInfo.visibility = View.GONE
            }
        }
    }

    private fun buildCurrentSnapshot(): SettingsSnapshot {
        return SettingsSnapshot(
            fontType = prefs.getString("font_type", "system") ?: "system",
            soundTheme = prefs.getString("sound_theme", "system") ?: "system",
            minecraftUI = cbMinecraftUI.isChecked,
            useDefaultPath = cbUseDefaultPath.isChecked,
            separatePaths = cbSeparatePaths.isChecked,
            noSubfolders = cbNoSubfolders.isChecked,
            dontOpenFile = cbDontOpenFile.isChecked,
            disableLogs = cbDisableLogs.isChecked,
            infiniteLogs = cbInfiniteLogs.isChecked,
            maxLogDays = prefs.getInt("max_log_days", 365),
            videoPath = if (!cbUseDefaultPath.isChecked) normalizeUserPath(etVideoPath.text.toString()) else prefs.getString("video_path", null),
            musicPath = if (!cbUseDefaultPath.isChecked && cbSeparatePaths.isChecked) normalizeUserPath(etMusicPath.text.toString()) else prefs.getString("music_path", null)
        )
    }

    private fun ensureCustomPathDefaults() {
        if (prefs.getString("video_path", null).isNullOrBlank()) {
            prefs.edit().putString("video_path", ROOT_PATH).putString("video_path_last_valid", ROOT_PATH).apply()
        }
        if (prefs.getString("music_path", null).isNullOrBlank()) {
            prefs.edit().putString("music_path", ROOT_PATH).putString("music_path_last_valid", ROOT_PATH).apply()
        }
    }

    private fun normalizeUserPath(raw: String?): String {
        val value = raw?.trim() ?: return ROOT_PATH
        if (value == "Используется путь по умолчанию") return ROOT_PATH
        return if (value.startsWith("/")) value else "$ROOT_PATH$value"
    }

    private fun savePickedFolder(uri: Uri, isMusic: Boolean) {
        contentResolver.takePersistableUriPermission(uri, Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_GRANT_WRITE_URI_PERMISSION)
        val absolute = treeUriToAbsolutePath(uri) ?: ROOT_PATH
        prefs.edit().putString(if (isMusic) "music_path_uri" else "video_path_uri", uri.toString()).putBoolean("use_default_path", false).apply()

        isUpdatingUI = true
        cbUseDefaultPath.isChecked = false
        isUpdatingUI = false

        saveValidPath(isMusic, absolute)
        if (!isMusic && !cbSeparatePaths.isChecked) saveValidPath(true, absolute)
        updateUI()
    }

    private fun saveValidPath(isMusic: Boolean, absolutePath: String) {
        val normalized = normalizeUserPath(absolutePath)
        prefs.edit().putString(if (isMusic) "music_path" else "video_path", normalized).putString(if (isMusic) "music_path_last_valid" else "video_path_last_valid", normalized).apply()
        if (isMusic) etMusicPath.setText(normalized) else etVideoPath.setText(normalized)
    }

    private fun treeUriToAbsolutePath(uri: Uri): String? {
        return try {
            val docId = DocumentsContract.getTreeDocumentId(uri)
            if (docId.startsWith("primary:")) {
                val relative = docId.removePrefix("primary:").trim('/')
                if (relative.isBlank()) ROOT_PATH else "$ROOT_PATH$relative"
            } else null
        } catch (_: Exception) { null }
    }

    private fun exportLogs() {
        val logFile = LogMaintenance.combinedLogFile(this)
        if (!logFile.exists()) return
        pendingExportLogFile = logFile
        exportLogLauncher.launch("combined_app.log")
    }
}