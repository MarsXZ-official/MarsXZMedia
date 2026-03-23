package com.marsxz.marsxzmedia

import android.content.ActivityNotFoundException
import android.content.Context
import android.content.Intent
import android.content.SharedPreferences
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.Environment
import android.provider.DocumentsContract
import android.provider.Settings
import android.view.View
import android.widget.Button
import android.widget.CheckBox
import android.widget.EditText
import android.widget.ImageButton
import android.widget.LinearLayout
import android.widget.TextView
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AlertDialog
import androidx.appcompat.app.AppCompatActivity
import java.io.File
import java.io.FileInputStream
import android.Manifest
import android.content.pm.PackageManager
import androidx.core.content.ContextCompat

class SettingsActivity : AppCompatActivity() {

    companion object {
        private const val ROOT_PATH = "/storage/emulated/0/"
    }

    private var initialSnapshot: SettingsSnapshot? = null
    private var changeMessageShown = false
    private var requestedManageStorage = false

    private data class SettingsSnapshot(
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

    private lateinit var backButton: ImageButton

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
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_settings)

        UiSoundPlayer.init(this)

        prefs = getSharedPreferences("app_settings", Context.MODE_PRIVATE)

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
        saveAndClose()
    }

    override fun onPause() {
        super.onPause()
        val days = etMaxDays.text.toString().toIntOrNull() ?: 365
        prefs.edit().putInt("max_log_days", days).apply()
        LogMaintenance.enforcePolicy(this)
    }

    private fun initViews() {
        backButton = findViewById(R.id.backButton)

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
        tvErrorInfo = findViewById(R.id.tvErrorInfo)
    }

    private fun ensureDefaultSettings() {
        val editor = prefs.edit()

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

        // Сравниваем каждое поле
        return cbUseDefaultPath.isChecked != snapshot.useDefaultPath ||
                cbSeparatePaths.isChecked != snapshot.separatePaths ||
                cbNoSubfolders.isChecked != snapshot.noSubfolders ||
                cbDontOpenFile.isChecked != snapshot.dontOpenFile ||
                cbDisableLogs.isChecked != snapshot.disableLogs ||
                cbInfiniteLogs.isChecked != snapshot.infiniteLogs ||
                etMaxDays.text.toString().toIntOrNull() != snapshot.maxLogDays
    }

    private fun setupListeners() {
        backButton.setOnClickListener {
            // 1. Всегда играем клик в начале
            UiSoundPlayer.playClick()

            if (hasChanges()) {
                // ЕСЛИ БЫЛИ ИЗМЕНЕНИЯ:
                it.postDelayed({
                    UiSoundPlayer.playApply() // Звук применения
                    SettingsNotificationHelper.showSettingsSaved(this) // Уведомление

                    it.postDelayed({
                        saveAndClose()
                    }, 150)
                }, 100)
            } else {
                // ЕСЛИ НИЧЕГО НЕ МЕНЯЛОСЬ:
                it.postDelayed({
                    finish() // Просто выходим без уведомлений
                }, 100)
            }
        }

        cbUseDefaultPath.setOnCheckedChangeListener { _, isChecked ->
            UiSoundPlayer.playClick() // Добавляем звук
            if (isUpdatingUI) return@setOnCheckedChangeListener

            if (isChecked) {
                prefs.edit().putBoolean("use_default_path", true).apply()
                updateUI()
                return@setOnCheckedChangeListener
            }

            ensureCustomPathDefaults()

            if (hasCustomPathAccess()) {
                prefs.edit().putBoolean("use_default_path", false).apply()
                Toast.makeText(this, "Разрешение предоставлено", Toast.LENGTH_SHORT).show()
                updateUI()
            } else {
                requestCustomPathAccess()
            }
        }

        cbSeparatePaths.setOnCheckedChangeListener { _, isChecked ->
            UiSoundPlayer.playClick() // Добавляем звук
            if (isUpdatingUI) return@setOnCheckedChangeListener
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
            UiSoundPlayer.playClick()
            selectVideoDirLauncher.launch(null)
        }

        btnSelectMusicPath.setOnClickListener {
            UiSoundPlayer.playClick()
            selectMusicDirLauncher.launch(null)
        }

        btnExportLogs.setOnClickListener {
            UiSoundPlayer.playClick() // ДОБАВИТЬ ЗВУК
            exportLogs()
        }

        cbNoSubfolders.setOnCheckedChangeListener { _, checked ->
            UiSoundPlayer.playClick() // ДОБАВИТЬ ЗВУК
            prefs.edit().putBoolean("no_subfolders", checked).apply()
        }

        cbDontOpenFile.setOnCheckedChangeListener { _, checked ->
            UiSoundPlayer.playClick() // ДОБАВИТЬ ЗВУК
            prefs.edit().putBoolean("dont_open_file", checked).apply()
        }

        cbDisableLogs.setOnCheckedChangeListener { _, checked ->
            UiSoundPlayer.playClick() // Добавляем звук
            prefs.edit().putBoolean("disable_logs", checked).apply()
            LogMaintenance.enforcePolicy(this)
            updateUI()
        }

        cbInfiniteLogs.setOnCheckedChangeListener { _, checked ->
            UiSoundPlayer.playClick() // Добавляем звук
            prefs.edit().putBoolean("infinite_logs", checked).apply()

            if (!checked) {
                val current = etMaxDays.text.toString().toIntOrNull()
                if (current == null || current <= 0) {
                    etMaxDays.setText("365")
                    prefs.edit().putInt("max_log_days", 365).apply()
                }
            }

            LogMaintenance.enforcePolicy(this)
            updateUI()
        }

        etVideoPath.setOnFocusChangeListener { _, hasFocus ->
            if (!hasFocus && !cbUseDefaultPath.isChecked) {
                applyManualPathFromField(isMusic = false, askCreate = true)
            }
        }

        etMusicPath.setOnFocusChangeListener { _, hasFocus ->
            if (!hasFocus && !cbUseDefaultPath.isChecked && cbSeparatePaths.isChecked) {
                applyManualPathFromField(isMusic = true, askCreate = true)
            }
        }
    }

    private fun hasCustomPathAccess(): Boolean {
        return when {
            Build.VERSION.SDK_INT >= Build.VERSION_CODES.R -> {
                Environment.isExternalStorageManager()
            }
            Build.VERSION.SDK_INT >= Build.VERSION_CODES.M -> {
                ContextCompat.checkSelfPermission(
                    this,
                    Manifest.permission.WRITE_EXTERNAL_STORAGE
                ) == PackageManager.PERMISSION_GRANTED
            }
            else -> true
        }
    }

    private fun requestCustomPathAccess() {
        when {
            Build.VERSION.SDK_INT >= Build.VERSION_CODES.R -> {
                requestedManageStorage = true
                try {
                    val intent = Intent(Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION).apply {
                        data = Uri.parse("package:$packageName")
                    }
                    startActivity(intent)
                } catch (_: ActivityNotFoundException) {
                    try {
                        startActivity(Intent(Settings.ACTION_MANAGE_ALL_FILES_ACCESS_PERMISSION))
                    } catch (_: Exception) {
                        revertToDefaultPath("Не удалось открыть настройки доступа к файлам")
                    }
                }
            }

            Build.VERSION.SDK_INT >= Build.VERSION_CODES.M -> {
                requestWriteStorageLauncher.launch(Manifest.permission.WRITE_EXTERNAL_STORAGE)
            }
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

        if (cbSeparatePaths.isChecked && !useDefault) {
            tvVideoPathLabel.text = "Путь к Video:"
            layoutMusicPath.visibility = View.VISIBLE
        } else {
            tvVideoPathLabel.text = "Путь к Video и Audio:"
            layoutMusicPath.visibility = View.GONE
        }

        if (cbDisableLogs.isChecked) {
            btnExportLogs.isEnabled = true
            etMaxDays.isEnabled = false
            cbInfiniteLogs.isEnabled = false
            tvErrorInfo.visibility = View.VISIBLE
            tvErrorInfo.text = "Логи отключены. Новые записи не будут создаваться."
            tvErrorInfo.setTextColor(android.graphics.Color.GRAY)
        } else {
            btnExportLogs.isEnabled = true
            cbInfiniteLogs.isEnabled = true
            etMaxDays.isEnabled = !cbInfiniteLogs.isChecked

            if (cbInfiniteLogs.isChecked) {
                tvErrorInfo.visibility = View.VISIBLE
                tvErrorInfo.text = "Авто-удаление отключено (вечное хранилище)"
                tvErrorInfo.setTextColor(android.graphics.Color.GRAY)
            } else {
                tvErrorInfo.visibility = View.GONE
            }
        }
    }

    private fun buildCurrentSnapshot(): SettingsSnapshot {
        return SettingsSnapshot(
            useDefaultPath = cbUseDefaultPath.isChecked,
            separatePaths = cbSeparatePaths.isChecked,
            noSubfolders = cbNoSubfolders.isChecked,
            dontOpenFile = cbDontOpenFile.isChecked,
            disableLogs = cbDisableLogs.isChecked,
            infiniteLogs = cbInfiniteLogs.isChecked,
            videoPath = prefs.getString("video_path", null),
            musicPath = prefs.getString("music_path", null), // Добавлена запятая
            maxLogDays = prefs.getInt("max_log_days", 365)  // Удален дубликат
        )
    }

    private fun notifyIfSettingsChanged() {
        if (changeMessageShown) return

        val before = initialSnapshot ?: return
        val after = buildCurrentSnapshot()

        if (before != after) {
            changeMessageShown = true
            UiSoundPlayer.playApply()
            SettingsNotificationHelper.showSettingsSaved(this)
        }
    }

    private fun saveAndClose() {
        applyManualPathFromField(isMusic = false, askCreate = false)
        if (cbSeparatePaths.isChecked && !cbUseDefaultPath.isChecked) {
            applyManualPathFromField(isMusic = true, askCreate = false)
        }

        val days = etMaxDays.text.toString().toIntOrNull() ?: 365
        prefs.edit().putInt("max_log_days", days).apply()
        LogMaintenance.enforcePolicy(this)

        notifyIfSettingsChanged()
        finish()
    }

    private fun applyManualPathFromField(isMusic: Boolean, askCreate: Boolean): Boolean {
        val field = if (isMusic) etMusicPath else etVideoPath
        val rawInput = field.text?.toString().orEmpty()

        return validateOrPrepareFolderPath(rawInput, askCreate) { validPath ->
            saveValidPath(isMusic, validPath)

            if (!isMusic && !cbSeparatePaths.isChecked) {
                saveValidPath(true, validPath)
                etMusicPath.setText(validPath)
            }

            if (isMusic) {
                val videoPath = prefs.getString("video_path", ROOT_PATH) ?: ROOT_PATH
                if (normalizeUserPath(videoPath) == normalizeUserPath(validPath)) {
                    isUpdatingUI = true
                    cbSeparatePaths.isChecked = false
                    prefs.edit().putBoolean("separate_paths", false).apply()
                    isUpdatingUI = false
                    updateUI()
                }
            }
        }
    }

    private fun validateOrPrepareFolderPath(
        rawInput: String,
        askCreate: Boolean,
        onReady: (String) -> Unit
    ): Boolean {
        val normalized = normalizeUserPath(rawInput)
        val folder = File(normalized)

        if (folder.exists() && folder.isDirectory) {
            onReady(normalized)
            return true
        }

        if (!askCreate) {
            return false
        }

        AlertDialog.Builder(this)
            .setTitle("Папка не найдена")
            .setMessage("Создать папку?\n$normalized")
            .setPositiveButton("Создать") { _, _ ->
                if (folder.mkdirs() || folder.exists()) {
                    onReady(normalized)
                    UiSoundPlayer.playApply()
                    SettingsNotificationHelper.showSettingsSaved(this)
                } else {
                    showSettingsError("Не удалось создать папку")
                }
            }
            .setNegativeButton("Отмена") { _, _ ->
                showSettingsError("Путь не применён")
                restorePreviousPath(rawInput == etMusicPath.text?.toString(), if (rawInput == etMusicPath.text?.toString()) etMusicPath else etVideoPath)
            }
            .show()

        return false
    }

    private fun savePickedFolder(uri: Uri, isMusic: Boolean) {
        contentResolver.takePersistableUriPermission(
            uri,
            Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_GRANT_WRITE_URI_PERMISSION
        )

        val absolute = treeUriToAbsolutePath(uri) ?: ROOT_PATH

        prefs.edit()
            .putString(if (isMusic) "music_path_uri" else "video_path_uri", uri.toString())
            .putBoolean("use_default_path", false)
            .apply()

        isUpdatingUI = true
        cbUseDefaultPath.isChecked = false
        isUpdatingUI = false

        saveValidPath(isMusic, absolute)

        if (!isMusic && !cbSeparatePaths.isChecked) {
            saveValidPath(true, absolute)
        }

        updateUI()
    }

    private fun saveValidPath(isMusic: Boolean, absolutePath: String) {
        val normalized = normalizeUserPath(absolutePath)
        val pathKey = if (isMusic) "music_path" else "video_path"
        val lastValidKey = if (isMusic) "music_path_last_valid" else "video_path_last_valid"

        prefs.edit()
            .putString(pathKey, normalized)
            .putString(lastValidKey, normalized)
            .apply()

        if (isMusic) {
            etMusicPath.setText(normalized)
        } else {
            etVideoPath.setText(normalized)
        }
    }

    private fun ensureCustomPathDefaults() {
        val currentVideo = prefs.getString("video_path", null)
        val currentMusic = prefs.getString("music_path", null)

        val editor = prefs.edit()
        if (currentVideo.isNullOrBlank()) {
            editor.putString("video_path", ROOT_PATH)
            editor.putString("video_path_last_valid", ROOT_PATH)
        }
        if (currentMusic.isNullOrBlank()) {
            editor.putString("music_path", ROOT_PATH)
            editor.putString("music_path_last_valid", ROOT_PATH)
        }
        editor.apply()
    }

    private fun restorePreviousPath(isMusic: Boolean, field: EditText) {
        val lastValidKey = if (isMusic) "music_path_last_valid" else "video_path_last_valid"
        val fallback = prefs.getString(lastValidKey, ROOT_PATH) ?: ROOT_PATH
        field.setText(fallback)
    }

    private fun findCombinedLogFile(): File? {
        val file = LogMaintenance.combinedLogFile(this)
        return file.takeIf { it.exists() && it.isFile }
    }

    private fun exportLogs() {
        val logFile = findCombinedLogFile()

        if (logFile == null) {
            tvErrorInfo.visibility = View.VISIBLE
            tvErrorInfo.text = "Файл combined_app.log не найден"
            tvErrorInfo.setTextColor(android.graphics.Color.RED)
            return
        }

        pendingExportLogFile = logFile
        exportLogLauncher.launch("combined_app.log")
    }

    private fun treeUriToAbsolutePath(uri: Uri): String? {
        return try {
            val docId = DocumentsContract.getTreeDocumentId(uri)
            if (docId.startsWith("primary:")) {
                val relative = docId.removePrefix("primary:").trim('/')
                if (relative.isBlank()) ROOT_PATH else "$ROOT_PATH$relative"
            } else {
                null
            }
        } catch (_: Exception) {
            null
        }
    }

    private fun normalizeUserPath(raw: String?): String {
        val value = raw?.trim().orEmpty()
        if (value.isBlank()) return ROOT_PATH

        val cleaned = value.replace('\\', '/').trim()

        return when {
            cleaned.startsWith("/storage/emulated/0/") -> cleaned
            cleaned == "/storage/emulated/0" -> ROOT_PATH
            cleaned.startsWith("/sdcard/") -> cleaned.replaceFirst("/sdcard", "/storage/emulated/0")
            cleaned == "/sdcard" -> ROOT_PATH
            cleaned.startsWith("/") -> cleaned
            else -> "$ROOT_PATH${cleaned.trimStart('/')}"
        }
    }

    private fun hasManageAllFilesAccess(): Boolean {
        return Build.VERSION.SDK_INT < Build.VERSION_CODES.R || Environment.isExternalStorageManager()
    }

    private fun openManageFilesAccessSettings() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.R) return

        requestedManageStorage = true

        try {
            val intent = Intent(Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION).apply {
                data = Uri.parse("package:$packageName")
            }
            startActivity(intent)
        } catch (_: ActivityNotFoundException) {
            try {
                startActivity(Intent(Settings.ACTION_MANAGE_ALL_FILES_ACCESS_PERMISSION))
            } catch (_: Exception) {
                Toast.makeText(this, "Не удалось открыть настройки доступа к файлам", Toast.LENGTH_LONG).show()
            }
        }
    }

    private fun showSettingsError(message: String) {
        tvErrorInfo.visibility = View.VISIBLE
        tvErrorInfo.text = message
        tvErrorInfo.setTextColor(android.graphics.Color.RED)
    }
}