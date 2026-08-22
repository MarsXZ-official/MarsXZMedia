package com.marsxz.marsxzmedia

import android.Manifest
import android.content.ActivityNotFoundException
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.BitmapFactory
import android.graphics.Color
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.view.View
import android.webkit.MimeTypeMap
import android.widget.*
import androidx.activity.enableEdgeToEdge
import androidx.appcompat.app.AppCompatActivity
import androidx.core.app.ActivityCompat
import androidx.core.content.ContextCompat
import androidx.core.content.FileProvider
import androidx.core.view.ViewCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import androidx.core.widget.doAfterTextChanged
import com.google.android.material.appbar.MaterialToolbar
import com.google.android.material.bottomnavigation.BottomNavigationView
import java.io.File
import java.net.HttpURLConnection
import java.net.URL

class MainActivity : AppCompatActivity() {

    private lateinit var urlBox: EditText
    private lateinit var pasteButton: Button
    private lateinit var findButton: Button

    private lateinit var homeScroll: View
    private lateinit var bottomNavigation: BottomNavigationView
    private var historyFragment: HistoryFragment? = null

    private lateinit var infoPanel: LinearLayout
    private lateinit var previewImage: ImageView
    private lateinit var videoTitle: TextView
    private lateinit var videoDescription: TextView
    private lateinit var videoAuthor: TextView
    private lateinit var videoDuration: TextView

    private lateinit var downloadTypeSpinner: Spinner
    private lateinit var qualityOrBitrateLabel: TextView
    private lateinit var qualityOrBitrateSpinner: Spinner
    private lateinit var audioLabel: TextView
    private lateinit var audioSpinner: Spinner
    private lateinit var downloadButton: Button
    private lateinit var topToolbar: MaterialToolbar

    private val allVideoQualities = listOf(
        VideoFormatsInfo.QualityItem("2160p (4К)", 2160),
        VideoFormatsInfo.QualityItem("1440p (2К)", 1440),
        VideoFormatsInfo.QualityItem("1080p (FHD)", 1080),
        VideoFormatsInfo.QualityItem("720p (HD)", 720),
        VideoFormatsInfo.QualityItem("480p (SD)", 480),
        VideoFormatsInfo.QualityItem("360p (SD)", 360),
        VideoFormatsInfo.QualityItem("240p (SD)", 240),
        VideoFormatsInfo.QualityItem("144p (SD)", 144)
    )

    private val audioBitrates = listOf(
        "64 kbps", "96 kbps", "128 kbps", "160 kbps",
        "192 kbps", "224 kbps", "256 kbps", "320 kbps"
    )

    private var currentVideoQualities: List<VideoFormatsInfo.QualityItem> = allVideoQualities
    private var currentAudioTracks: List<String> = listOf("Авто")

    private var isSearchInProgress = false
    private var isDownloadInProgress = false
    private var hasResolvedVideo = false
    private var resolvedUrl: String? = null

    private var currentAppliedFont: String? = null
    private var currentAppliedSquare: Boolean = false

    override fun onCreate(savedInstanceState: Bundle?) {
        val prefs = getSharedPreferences("app_settings", Context.MODE_PRIVATE)
        val fontType = prefs.getString("font_type", "system")
        val isSquare = prefs.getBoolean("minecraft_ui", false)
        
        currentAppliedFont = fontType
        currentAppliedSquare = isSquare

        // Apply theme based on combination of Font and Square UI settings
        when {
            isSquare && fontType == "monocraft" -> setTheme(R.style.Theme_MarsXZMedia_Square_Monocraft)
            isSquare -> setTheme(R.style.Theme_MarsXZMedia_Square)
            fontType == "monocraft" -> setTheme(R.style.Theme_MarsXZMedia_Monocraft)
            else -> setTheme(R.style.Theme_MarsXZMedia)
        }

        enableEdgeToEdge()
        super.onCreate(savedInstanceState)

        setContentView(R.layout.activity_main)

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

        ViewCompat.setOnApplyWindowInsetsListener(findViewById(R.id.bottomNavigation)) { v, insets ->
            val bars = insets.getInsets(WindowInsetsCompat.Type.systemBars())
            v.setPadding(v.paddingLeft, v.paddingTop, v.paddingRight, bars.bottom)
            insets
        }

        UiSoundPlayer.init(this)

        AppPaths.ensureDirectories(this)
        LogMaintenance.enforcePolicy(this)
        AppLog.write(this, "I", "=== ПРИЛОЖЕНИЕ ЗАПУЩЕНО ===")
        DownloadNotificationHelper.ensureChannel(this)
        ensureNotificationPermission()

        bindViews()
        setupTopMenu()
        setupSpinners()
        setupUrlValidation()
        setupPasteButton()
        setupFindButton()
        setupDownloadButton()
        setupBottomNavigation()

        infoPanel.visibility = View.GONE
        showHomeScreen()
        updateActionButtonsState()
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

    override fun onDestroy() {
        super.onDestroy()
        UiSoundPlayer.release()
    }

    private fun setupTopMenu() {
        topToolbar.overflowIcon?.setTint(android.graphics.Color.parseColor("#333333"))
        topToolbar.setOnMenuItemClickListener { item ->
            when (item.itemId) {
                R.id.action_settings -> {
                    UiSoundPlayer.playClick(this)
                    startActivity(Intent(this, SettingsActivity::class.java))
                    true
                }
                R.id.action_about -> {
                    UiSoundPlayer.playClick(this)
                    startActivity(Intent(this, AboutActivity::class.java))
                    true
                }
                else -> false
            }
        }
    }

    private fun bindViews() {
        homeScroll = findViewById(R.id.homeScroll)
        bottomNavigation = findViewById(R.id.bottomNavigation)
        urlBox = findViewById(R.id.urlBox)
        topToolbar = findViewById(R.id.topToolbar)
        pasteButton = findViewById(R.id.pasteButton)
        findButton = findViewById(R.id.findButton)
        infoPanel = findViewById(R.id.infoPanel)
        previewImage = findViewById(R.id.previewImage)
        videoTitle = findViewById(R.id.videoTitle)
        videoDescription = findViewById(R.id.videoDescription)
        videoAuthor = findViewById(R.id.videoAuthor)
        videoDuration = findViewById(R.id.videoDuration)
        downloadTypeSpinner = findViewById(R.id.downloadTypeSpinner)
        qualityOrBitrateLabel = findViewById(R.id.qualityOrBitrateLabel)
        qualityOrBitrateSpinner = findViewById(R.id.qualityOrBitrateSpinner)
        audioLabel = findViewById(R.id.audioLabel)
        audioSpinner = findViewById(R.id.audioSpinner)
        downloadButton = findViewById(R.id.downloadButton)
    }

    private fun setupBottomNavigation() {
        bottomNavigation.setOnItemSelectedListener { item ->
            UiSoundPlayer.playClick(this)
            when (item.itemId) {
                R.id.nav_home -> { showHomeScreen(); true }
                R.id.nav_history -> { showHistoryScreen(); true }
                else -> false
            }
        }
        bottomNavigation.selectedItemId = R.id.nav_home
    }

    private fun showHomeScreen() {
        homeScroll.visibility = View.VISIBLE
        findViewById<View>(R.id.historyContainer).visibility = View.GONE
    }

    private fun shouldAutoOpenDownloadedFile(): Boolean {
        val prefs = getSharedPreferences("app_settings", Context.MODE_PRIVATE)
        return !prefs.getBoolean("dont_open_file", false)
    }

    private fun openDownloadedFileIfAllowed(file: File) {
        if (!shouldAutoOpenDownloadedFile()) return
        openDownloadedFile(file)
    }

    private fun openDownloadedFile(file: File) {
        try {
            val uri = FileProvider.getUriForFile(this, "$packageName.fileprovider", file)
            val mime = MimeTypeMap.getSingleton().getMimeTypeFromExtension(file.extension.lowercase()) ?: "*/*"
            val intent = Intent(Intent.ACTION_VIEW).apply {
                setDataAndType(uri, mime)
                addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            }
            startActivity(intent)
        } catch (_: ActivityNotFoundException) {
            showInlineNotification("Готово", "Файл сохранён, но приложение для открытия не найдено")
        } catch (e: Exception) {
            AppLog.write(this, "E", "Не удалось открыть файл: ${e.message}", "OpenFile", e)
        }
    }

    private fun showHistoryScreen() {
        homeScroll.visibility = View.GONE
        findViewById<View>(R.id.historyContainer).visibility = View.VISIBLE
        if (historyFragment == null) {
            historyFragment = HistoryFragment().apply {
                onEntrySelected = { entry ->
                    urlBox.setText(entry.url)
                    bottomNavigation.selectedItemId = R.id.nav_home
                    urlBox.postDelayed({ if (!isSearchInProgress && !isDownloadInProgress) findButton.performClick() }, 150)
                }
            }
            supportFragmentManager.beginTransaction().replace(R.id.historyContainer, historyFragment!!).commit()
        } else {
            historyFragment?.refreshView()
        }
    }
    
    private fun showInlineNotification(title: String, message: String) {
        DownloadNotificationHelper.showSimple(this, title, message)
    }

    private fun setupSpinners() {
        val typeAdapter = ArrayAdapter(this, android.R.layout.simple_spinner_item, listOf("Видео (MP4)", "Аудио (MP3)"))
        typeAdapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item)
        downloadTypeSpinner.adapter = typeAdapter

        applyAvailableQualities(null)
        applyAudioTrackLabels(listOf("Авто"))

        downloadTypeSpinner.onItemSelectedListener = object : AdapterView.OnItemSelectedListener {
            override fun onItemSelected(parent: AdapterView<*>?, view: View?, position: Int, id: Long) {
                val isAudio = position == 1
                if (isAudio) {
                    qualityOrBitrateLabel.text = "Выберите битрейт:"
                    val bitrateAdapter = ArrayAdapter(this@MainActivity, android.R.layout.simple_spinner_item, audioBitrates)
                    bitrateAdapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item)
                    qualityOrBitrateSpinner.adapter = bitrateAdapter
                    qualityOrBitrateSpinner.setSelection(audioBitrates.indexOf("320 kbps").coerceAtLeast(0))
                } else {
                    qualityOrBitrateLabel.text = "Выберите качество:"
                    applyAvailableQualities(currentVideoQualities)
                }
                val showAudio = currentAudioTracks.size > 1
                audioLabel.visibility = if (showAudio) View.VISIBLE else View.GONE
                audioSpinner.visibility = if (showAudio) View.VISIBLE else View.GONE
                updateActionButtonsState()
            }
            override fun onNothingSelected(parent: AdapterView<*>?) = Unit
        }
    }

    private fun isSupportedYoutubeUrl(url: String) = url.lowercase().run {
        startsWith("https://www.youtube.com") || startsWith("https://m.youtube.com") || startsWith("https://youtu.be")
    }

    private fun updateActionButtonsState() {
        val currentUrl = urlBox.text?.toString()?.trim().orEmpty()
        val canFind = !isSearchInProgress && !isDownloadInProgress && isSupportedYoutubeUrl(currentUrl)
        val canDownload = !isSearchInProgress && !isDownloadInProgress && hasResolvedVideo && resolvedUrl == currentUrl

        findButton.text = if (isSearchInProgress) "ПОИСК..." else "ИСКАТЬ ВИДЕО"
        downloadButton.text = if (isDownloadInProgress) "ЗАГРУЗКА..." else "СКАЧАТЬ РЕСУРСЫ"
        findButton.isEnabled = canFind
        downloadButton.isEnabled = canDownload
        urlBox.isEnabled = !isSearchInProgress && !isDownloadInProgress
        pasteButton.isEnabled = !isSearchInProgress && !isDownloadInProgress

        val isNight = (resources.configuration.uiMode and android.content.res.Configuration.UI_MODE_NIGHT_MASK) == android.content.res.Configuration.UI_MODE_NIGHT_YES
        val inactiveTextColor = ContextCompat.getColor(this, R.color.mc_button_inactive_text)
        val stoneTextColor = ContextCompat.getColor(this, R.color.mc_stone_text)

        applyButtonVisualState(findButton, canFind, R.drawable.btn_mc_youtube, Color.WHITE, inactiveTextColor)
        applyButtonVisualState(downloadButton, canDownload, R.drawable.btn_mc_green, Color.parseColor("#55FF55"), inactiveTextColor)
        
        // PASTE button visibility and text color
        applyButtonVisualState(pasteButton, true, R.drawable.btn_mc_stone, stoneTextColor, inactiveTextColor)
    }

    private fun applyButtonVisualState(button: Button, active: Boolean, res: Int, textColor: Int, inactiveColor: Int) {
        button.setBackgroundResource(if (active) res else R.drawable.btn_mc_inactive)
        button.setTextColor(if (active) textColor else inactiveColor)
    }

    private fun setupUrlValidation() {
        urlBox.doAfterTextChanged { text ->
            val url = text?.toString()?.trim().orEmpty()
            if (resolvedUrl != null && resolvedUrl != url) hasResolvedVideo = false
            urlBox.setBackgroundResource(when {
                url.isBlank() -> R.drawable.edittext_mc_style
                isSupportedYoutubeUrl(url) -> R.drawable.edittext_mc_valid
                else -> R.drawable.edittext_mc_invalid
            })
            updateActionButtonsState()
        }
    }

    private fun setupPasteButton() {
        pasteButton.setOnClickListener {
            UiSoundPlayer.playClick(this)
            val clipboard = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
            val text = clipboard.primaryClip?.getItemAt(0)?.coerceToText(this).toString()
            if (text.isNotBlank()) {
                urlBox.setText(text)
                urlBox.setSelection(0)
            } else Toast.makeText(this, "Буфер обмена пуст", Toast.LENGTH_SHORT).show()
        }
    }

    private fun setupFindButton() {
        findButton.setOnClickListener {
            UiSoundPlayer.playClick(this)
            val url = urlBox.text.toString().trim()
            if (url.isBlank() || isSearchInProgress || isDownloadInProgress) return@setOnClickListener
            isSearchInProgress = true
            hasResolvedVideo = false
            updateActionButtonsState()
            showInlineNotification("Анализ", "Получаю информацию о видео…")

            fetchVideoDataWithRetry(url) { metaResult, formatsResult ->
                isSearchInProgress = false
                val info = metaResult.getOrNull()
                val isAudioRequest = downloadTypeSpinner.selectedItemPosition == 1
                
                if (info != null) {
                    resolvedUrl = url; hasResolvedVideo = true
                    infoPanel.visibility = View.VISIBLE
                    videoTitle.text = formatTitle(info.title)
                    HistoryStore.add(this, formatTitle(info.title), url)
                    videoDescription.text = formatDescription(info.description)
                    videoAuthor.text = "Автор: ${info.author}"
                    videoDuration.text = info.durationText
                    if (!info.thumbnailUrl.isNullOrBlank()) loadThumbnail(info.thumbnailUrl)
                } else {
                    infoPanel.visibility = View.GONE
                    val error = metaResult.exceptionOrNull()
                    val rawMsg = error?.message ?: "Unknown search error"
                    val code = if (rawMsg.contains("403")) "403" else if (rawMsg.contains("404")) "404" else "Error"
                    ErrorReporter.report(this, code, rawMsg, "Search", isAudioRequest)
                }

                val formats = formatsResult.getOrNull()
                if (formats != null) {
                    currentVideoQualities = formats.qualityItems
                    applyAudioTrackLabels(formats.audioTracks)
                    if (downloadTypeSpinner.selectedItemPosition == 0) applyAvailableQualities(currentVideoQualities)
                } else {
                    val error = formatsResult.exceptionOrNull()
                    if (hasResolvedVideo) { // Meta success but formats failed
                         val rawMsg = error?.message ?: "Unknown formats error"
                         val code = if (rawMsg.contains("403")) "403" else if (rawMsg.contains("404")) "404" else "FormatError"
                         ErrorReporter.report(this, code, rawMsg, "Formats", isAudioRequest)
                    }
                }
                updateActionButtonsState()
            }
        }
    }

    private fun fetchVideoDataWithRetry(url: String, onDone: (Result<VideoInfo>, Result<VideoFormatsInfo>) -> Unit) {
        Thread {
            var meta: Result<VideoInfo> = Result.failure(Exception())
            var formats: Result<VideoFormatsInfo> = Result.failure(Exception())
            for (attempt in 1..2) {
                meta = YoutubeSearchService.searchByUrl(url)
                formats = YoutubeFormatsService.loadFormats(url)
                if (meta.isSuccess && formats.isSuccess) break
                Thread.sleep(800)
            }
            runOnUiThread { onDone(meta, formats) }
        }.start()
    }

    private fun setupDownloadButton() {
        downloadButton.setOnClickListener {
            UiSoundPlayer.playClick(this)
            val url = urlBox.text.toString().trim()
            if (!hasResolvedVideo || resolvedUrl != url) return@setOnClickListener
            val isAudio = downloadTypeSpinner.selectedItemPosition == 1
            isDownloadInProgress = true; updateActionButtonsState()
            Thread {
                val res = MediaDownloadManager.download(this, MediaDownloadManager.Request(
                    url = url, isAudio = isAudio, title = videoTitle.text.toString(),
                    selectedQualityLabel = if (!isAudio) qualityOrBitrateSpinner.selectedItem?.toString() else null,
                    selectedBitrateLabel = if (isAudio) qualityOrBitrateSpinner.selectedItem?.toString() else null,
                    selectedAudioTrackLabel = if (audioSpinner.visibility == View.VISIBLE) audioSpinner.selectedItem?.toString() else null
                ))
                runOnUiThread {
                    isDownloadInProgress = false; updateActionButtonsState()
                    res.onSuccess { UiSoundPlayer.playApply(this); openDownloadedFileIfAllowed(it) }
                       .onFailure {
                           val rawMsg = it.message ?: "Download failed"
                           val code = if (rawMsg.contains("403")) "403" else if (rawMsg.contains("404")) "404" else "DL-Err"
                           ErrorReporter.report(this, code, rawMsg, "Download", isAudio)
                       }
                }
            }.start()
        }
    }

    private fun formatTitle(raw: String) = raw.replace(Regex("[-_|/]"), " ").replace(Regex("\\s+"), " ").trim()

    private fun formatDescription(raw: String?) = raw?.replace(Regex("[\\n\\r\\t]+"), " ")?.take(150)?.trim() ?: ""

    private fun loadThumbnail(url: String) {
        Thread {
            try {
                val conn = URL(url).openConnection() as HttpURLConnection
                conn.setRequestProperty("User-Agent", DownloaderImpl.DESKTOP_UA)
                conn.connectTimeout = 10000
                conn.readTimeout = 10000
                conn.inputStream.use { 
                    val bitmap = BitmapFactory.decodeStream(it)
                    runOnUiThread { previewImage.setImageBitmap(bitmap) }
                }
            } catch (e: Exception) {
                AppLog.write(this, "E", "Ошибка загрузки превью: ${e.message}")
            }
        }.start()
    }

    private fun applyAvailableQualities(qualities: List<VideoFormatsInfo.QualityItem>?) {
        val list = if (qualities.isNullOrEmpty()) allVideoQualities else qualities
        val adapter = ArrayAdapter(this, android.R.layout.simple_spinner_item, list.map { it.label })
        adapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item)
        qualityOrBitrateSpinner.adapter = adapter
        qualityOrBitrateSpinner.setSelection(0)
    }

    private fun applyAudioTrackLabels(tracks: List<String>) {
        val list = if (tracks.isEmpty()) listOf("Авто") else tracks
        val adapter = ArrayAdapter(this, android.R.layout.simple_spinner_item, list)
        adapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item)
        audioSpinner.adapter = adapter
        audioLabel.visibility = if (list.size > 1) View.VISIBLE else View.GONE
        audioSpinner.visibility = if (list.size > 1) View.VISIBLE else View.GONE
    }

    private fun ensureNotificationPermission() {
        if (Build.VERSION.SDK_INT >= 33 && ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
            ActivityCompat.requestPermissions(this, arrayOf(Manifest.permission.POST_NOTIFICATIONS), 1001)
        }
    }
}
