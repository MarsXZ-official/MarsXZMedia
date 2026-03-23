package com.marsxz.marsxzmedia

import android.Manifest
import android.content.ClipboardManager
import android.content.Context
import android.content.pm.PackageManager
import android.graphics.BitmapFactory
import android.graphics.Color
import android.os.Build
import android.os.Bundle
import android.view.View
import android.widget.AdapterView
import android.widget.ArrayAdapter
import android.widget.Button
import android.widget.EditText
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.Spinner
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import com.google.android.material.bottomnavigation.BottomNavigationView
import com.google.android.material.appbar.MaterialToolbar
import androidx.core.app.ActivityCompat
import androidx.core.content.ContextCompat
import androidx.core.widget.doAfterTextChanged
import java.net.URL
import android.content.ActivityNotFoundException
import android.content.Intent
import android.net.Uri
import android.webkit.MimeTypeMap
import androidx.core.content.FileProvider
import androidx.core.view.WindowInsetsControllerCompat
import android.view.WindowManager
import java.io.File

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
        setContentView(R.layout.activity_main)

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

    override fun onDestroy() {
        super.onDestroy()
        UiSoundPlayer.release()
    }

    private fun setupTopMenu() {
        topToolbar.overflowIcon?.setTint(android.graphics.Color.parseColor("#333333"))

        topToolbar.setOnMenuItemClickListener { item ->
            when (item.itemId) {
                R.id.action_settings -> {
                    UiSoundPlayer.playClick()
                    startActivity(Intent(this, SettingsActivity::class.java))
                    true
                }

                R.id.action_about -> {
                    UiSoundPlayer.playClick()
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
            UiSoundPlayer.playClick() // ДОБАВИТЬ ЗВУК при смене вкладки
            when (item.itemId) {
                R.id.nav_home -> {
                    showHomeScreen()
                    true
                }

                R.id.nav_history -> {
                    showHistoryScreen()
                    true
                }

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
            val uri = FileProvider.getUriForFile(
                this,
                "$packageName.fileprovider",
                file
            )

            val extension = file.extension.lowercase()
            val mime = MimeTypeMap.getSingleton()
                .getMimeTypeFromExtension(extension)
                ?: "*/*"

            val intent = Intent(Intent.ACTION_VIEW).apply {
                setDataAndType(uri, mime)
                addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            }

            startActivity(intent)
        } catch (_: ActivityNotFoundException) {
            showInlineNotification("Готово", "Файл сохранён, но приложение для открытия не найдено")
        } catch (e: Exception) {
            AppLog.write(this, "E", "Не удалось открыть скачанный файл: ${e.message}", "OpenFile", e)
            showInlineNotification("Готово", "Файл сохранён, но открыть его не удалось")
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

                    urlBox.postDelayed({
                        if (!isSearchInProgress && !isDownloadInProgress) {
                            findButton.performClick()
                        }
                    }, 150)
                }
            }

            supportFragmentManager.beginTransaction()
                .replace(R.id.historyContainer, historyFragment!!)
                .commit()
        } else {
            historyFragment?.refreshView()
        }
    }
    
    private fun showInlineNotification(title: String, message: String) {
        AppLog.write(this, "I", "Уведомление: $title - $message", "Notify")
        DownloadNotificationHelper.showSimple(this, title, message)
    }

    private fun setupSpinners() {
        val typeAdapter = ArrayAdapter(
            this,
            android.R.layout.simple_spinner_item,
            listOf("Видео (MP4)", "Аудио (MP3)")
        )
        typeAdapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item)
        downloadTypeSpinner.adapter = typeAdapter

        applyAvailableQualities(null)
        applyAudioTrackLabels(listOf("Авто"))

        downloadTypeSpinner.onItemSelectedListener = object : AdapterView.OnItemSelectedListener {
            override fun onItemSelected(parent: AdapterView<*>?, view: View?, position: Int, id: Long) {
                val isAudio = position == 1
                if (isAudio) {
                    qualityOrBitrateLabel.text = "Выберите битрейт:"
                    val bitrateAdapter = ArrayAdapter(
                        this@MainActivity,
                        android.R.layout.simple_spinner_item,
                        audioBitrates
                    )
                    bitrateAdapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item)
                    qualityOrBitrateSpinner.adapter = bitrateAdapter
                    qualityOrBitrateSpinner.setSelection(audioBitrates.indexOf("320 kbps").coerceAtLeast(0))
                } else {
                    qualityOrBitrateLabel.text = "Выберите качество:"
                    applyAvailableQualities(currentVideoQualities.maxOfOrNull { it.height })
                }

                val showAudio = currentAudioTracks.size > 1
                audioLabel.visibility = if (showAudio) View.VISIBLE else View.GONE
                audioSpinner.visibility = if (showAudio) View.VISIBLE else View.GONE
                updateActionButtonsState()
            }

            override fun onNothingSelected(parent: AdapterView<*>?) = Unit
        }
    }

    private fun isSupportedYoutubeUrl(url: String): Boolean {
        val lowerUrl = url.lowercase()
        return lowerUrl.startsWith("https://www.youtube.com") ||
            lowerUrl.startsWith("https://m.youtube.com") ||
            lowerUrl.startsWith("https://youtu.be")
    }

    private fun applyButtonVisualState(
        button: Button,
        active: Boolean,
        activeBackgroundRes: Int,
        activeTextColor: Int
    ) {
        if (active) {
            try {
                button.setBackgroundResource(activeBackgroundRes)
                button.setTextColor(activeTextColor)
            } catch (_: Exception) {
            }
        } else {
            try {
                button.setBackgroundResource(R.drawable.btn_mc_inactive)
                button.setTextColor(Color.parseColor("#757575"))
            } catch (_: Exception) {
            }
        }
    }

    private fun updateActionButtonsState() {
        val currentUrl = urlBox.text?.toString()?.trim().orEmpty()
        val canFind = !isSearchInProgress && !isDownloadInProgress && isSupportedYoutubeUrl(currentUrl)
        val canDownload = !isSearchInProgress && !isDownloadInProgress && hasResolvedVideo && resolvedUrl == currentUrl

        findButton.text = if (isSearchInProgress) "ПОИСК..." else "ИСКАТЬ ВИДЕО"
        downloadButton.text = if (isDownloadInProgress) "ЗАГРУЗКА..." else "СКАЧАТЬ РЕСУРСЫ"

        findButton.isEnabled = canFind
        downloadButton.isEnabled = canDownload

        applyButtonVisualState(findButton, canFind, R.drawable.btn_mc_youtube, Color.WHITE)
        applyButtonVisualState(downloadButton, canDownload, R.drawable.btn_mc_green, Color.parseColor("#55FF55"))
    }

    private fun setupUrlValidation() {
        urlBox.doAfterTextChanged { text ->
            val url = text?.toString()?.trim().orEmpty()
            val isValidYoutube = isSupportedYoutubeUrl(url)

            if (resolvedUrl != null && resolvedUrl != url) {
                hasResolvedVideo = false
                if (!isSearchInProgress && !isDownloadInProgress) {
                    infoPanel.visibility = View.GONE
                }
            }

            when {
                url.isBlank() -> {
                    try { urlBox.setBackgroundResource(R.drawable.edittext_mc_style) } catch (_: Exception) {}
                }
                isValidYoutube -> {
                    try { urlBox.setBackgroundResource(R.drawable.edittext_mc_valid) } catch (_: Exception) {}
                }
                else -> {
                    try { urlBox.setBackgroundResource(R.drawable.edittext_mc_invalid) } catch (_: Exception) {}
                }
            }

            updateActionButtonsState()
        }
    }

    private fun setupPasteButton() {
        pasteButton.setOnClickListener {
            UiSoundPlayer.playClick()

            val clipboard = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
            val clip = clipboard.primaryClip
            val text = if (clip != null && clip.itemCount > 0) {
                clip.getItemAt(0).coerceToText(this).toString()
            } else {
                ""
            }

            if (text.isNotBlank()) {
                urlBox.setText(text)
                urlBox.post { urlBox.setSelection(0) }
            } else {
                Toast.makeText(this, "Буфер обмена пуст", Toast.LENGTH_SHORT).show()
            }
        }
    }

    private fun setupFindButton() {
        findButton.setOnClickListener {
            UiSoundPlayer.playClick()
            val url = urlBox.text.toString().trim()
            if (url.isBlank() || isSearchInProgress || isDownloadInProgress) return@setOnClickListener

            isSearchInProgress = true
            hasResolvedVideo = false
            updateActionButtonsState()
            AppLog.write(this, "I", "Поиск видео: $url")
            showInlineNotification("Анализ", "Получаю информацию о видео…")

            fetchVideoDataWithRetry(url) { metaResult, formatsResult ->
                isSearchInProgress = false

                val info = metaResult.getOrNull()
                if (info != null) {
                    resolvedUrl = url
                    hasResolvedVideo = true
                    infoPanel.visibility = View.VISIBLE
                    videoTitle.text = formatTitle(info.title)

                    HistoryStore.add(this, formatTitle(info.title), url)

                    val formattedDescription = formatDescription(info.description)
                    if (formattedDescription.isBlank()) {
                        videoDescription.visibility = View.GONE
                    } else {
                        videoDescription.visibility = View.VISIBLE
                        videoDescription.text = formattedDescription
                    }

                    videoAuthor.text = "Автор: ${info.author}"
                    videoDuration.text = "Время: ${info.durationText}"

                    if (!info.thumbnailUrl.isNullOrBlank()) {
                        loadThumbnail(info.thumbnailUrl)
                    } else {
                        previewImage.setImageResource(android.R.drawable.ic_media_play)
                    }
                } else {
                    resolvedUrl = null
                    hasResolvedVideo = false
                    infoPanel.visibility = View.GONE
                    showInlineNotification("Ошибка", metaResult.exceptionOrNull()?.message ?: "Ошибка поиска видео")
                    updateActionButtonsState()
                    return@fetchVideoDataWithRetry
                }

                val formats = formatsResult.getOrNull()
                if (formats != null) {
                    currentVideoQualities = if (formats.qualityItems.isEmpty()) allVideoQualities else formats.qualityItems
                    applyAudioTrackLabels(formats.audioTracks)

                    if (downloadTypeSpinner.selectedItemPosition == 0) {
                        applyAvailableQualities(currentVideoQualities.maxOfOrNull { it.height })
                    }

                    AppLog.write(
                        this,
                        "I",
                        "Форматы получены: quality=${formats.qualityItems.map { it.label }}, audio=${formats.audioTracks}",
                        "Formats"
                    )
                } else {
                    AppLog.write(
                        this,
                        "W",
                        "Форматы не получены: ${formatsResult.exceptionOrNull()?.message}",
                        "Formats"
                    )
                    currentVideoQualities = allVideoQualities
                    applyAudioTrackLabels(listOf("Авто"))

                    if (downloadTypeSpinner.selectedItemPosition == 0) {
                        applyAvailableQualities(null)
                    }
                }

                showInlineNotification("Готово", "Информация о видео получена")
                updateActionButtonsState()
            }
        }
    }

    private fun looksBrokenText(text: String?): Boolean {
        if (text.isNullOrBlank()) return true

        val value = text.trim()
        if (value.isBlank()) return true

        val questionCount = value.count { it == '?' || it == '�' }
        val ratio = questionCount.toDouble() / value.length.coerceAtLeast(1)

        return ratio > 0.25
    }

    private fun isWeakFormatsResult(formats: VideoFormatsInfo?): Boolean {
        if (formats == null) return true

        val maxHeight = formats.qualityItems.maxOfOrNull { it.height } ?: 0
        val noAudioTracks = formats.audioTracks.isEmpty()

        return maxHeight <= 360 || noAudioTracks
    }

    private fun shouldRetryResult(info: VideoInfo?, formats: VideoFormatsInfo?): Boolean {
        val brokenTitle = looksBrokenText(info?.title)
        val brokenDescription = looksBrokenText(info?.description)
        val missingThumbnail = info?.thumbnailUrl.isNullOrBlank()
        val weakFormats = isWeakFormatsResult(formats)

        return brokenTitle || brokenDescription || missingThumbnail || weakFormats
    }

    private fun buildRetryReason(info: VideoInfo?, formats: VideoFormatsInfo?): String {
        val reasons = mutableListOf<String>()

        if (looksBrokenText(info?.title)) reasons += "битый заголовок"
        if (looksBrokenText(info?.description)) reasons += "битое описание"
        if (info?.thumbnailUrl.isNullOrBlank()) reasons += "нет превью"

        val maxHeight = formats?.qualityItems?.maxOfOrNull { it.height } ?: 0
        if (maxHeight <= 360) reasons += "максимум только ${maxHeight}p"
        if (formats?.audioTracks.isNullOrEmpty()) reasons += "нет звуковых дорожек"

        return if (reasons.isEmpty()) "неизвестная причина" else reasons.joinToString(", ")
    }

    private fun fetchVideoDataWithRetry(
        url: String,
        maxAttempts: Int = 2,
        delayMs: Long = 800,
        onDone: (Result<VideoInfo>, Result<VideoFormatsInfo>) -> Unit
    ) {
        Thread {
            var finalMeta: Result<VideoInfo> = Result.failure(IllegalStateException("Нет данных"))
            var finalFormats: Result<VideoFormatsInfo> = Result.failure(IllegalStateException("Нет данных"))

            for (attempt in 1..maxAttempts) {
                AppLog.write(this, "I", "Попытка получения данных #$attempt", "Fetch")

                val metaResult = YoutubeSearchService.searchByUrl(url)
                val formatsResult = YoutubeFormatsService.loadFormats(url)

                val info = metaResult.getOrNull()
                val formats = formatsResult.getOrNull()

                finalMeta = metaResult
                finalFormats = formatsResult

                val shouldRetry = shouldRetryResult(info, formats)

                if (!shouldRetry) {
                    AppLog.write(this, "I", "Попытка #$attempt успешна: данные выглядят нормальными", "Fetch")
                    break
                }

                val reason = buildRetryReason(info, formats)
                AppLog.write(this, "W", "Попытка #$attempt дала слабый/битый результат: $reason", "Fetch")

                if (attempt < maxAttempts) {
                    AppLog.write(
                        this,
                        "I",
                        "Повторный запрос через ${delayMs}мс. Возможная причина: слабый интернет, холодный старт extractor или неполная загрузка данных.",
                        "Fetch"
                    )
                    try {
                        Thread.sleep(delayMs)
                    } catch (_: Exception) {
                    }
                }
            }

            runOnUiThread {
                onDone(finalMeta, finalFormats)
            }
        }.start()
    }

    private fun setupDownloadButton() {
        downloadButton.setOnClickListener {
            UiSoundPlayer.playClick()
            val url = urlBox.text.toString().trim()
            if (url.isBlank()) {
                showInlineNotification("Ошибка", "Сначала вставьте ссылку")
                return@setOnClickListener
            }
            if (isSearchInProgress || isDownloadInProgress) return@setOnClickListener
            if (!hasResolvedVideo || resolvedUrl != url) {
                showInlineNotification("Ошибка", "Сначала нажмите ИСКАТЬ ВИДЕО для текущей ссылки")
                return@setOnClickListener
            }

            val isAudio = downloadTypeSpinner.selectedItemPosition == 1
            val selectedQuality = if (!isAudio) qualityOrBitrateSpinner.selectedItem?.toString() else null
            val selectedBitrate = if (isAudio) qualityOrBitrateSpinner.selectedItem?.toString() else null
            val selectedAudioTrack = if (audioSpinner.visibility == View.VISIBLE) audioSpinner.selectedItem?.toString() else null
            val title = videoTitle.text?.toString()?.trim().orEmpty().ifBlank { "Video" }

            isDownloadInProgress = true
            updateActionButtonsState()

            AppLog.write(
                this,
                "I",
                "Запуск загрузки: mode=${if (isAudio) "audio" else "video"}, quality=$selectedQuality, bitrate=$selectedBitrate, track=$selectedAudioTrack",
                "Download"
            )

            Thread {
                val result = MediaDownloadManager.download(
                    this,
                    MediaDownloadManager.Request(
                        url = url,
                        isAudio = isAudio,
                        title = title,
                        selectedQualityLabel = selectedQuality,
                        selectedBitrateLabel = selectedBitrate,
                        selectedAudioTrackLabel = selectedAudioTrack
                    )
                )

                runOnUiThread {
                    isDownloadInProgress = false
                    updateActionButtonsState()

                    result.onSuccess { file ->
                        AppLog.write(this, "I", "Загрузка завершена: ${file.absolutePath}", "Download")
                        UiSoundPlayer.playApply()
                        openDownloadedFileIfAllowed(file)
                    }.onFailure { e ->
                        AppLog.write(this, "E", "Ошибка загрузки", "Download", e)
                        showInlineNotification("Ошибка", e.message ?: "Ошибка загрузки")
                    }
                }
            }.start()
        }
    }

    private fun formatTitle(raw: String): String {
        var title = raw.replace(Regex("[-_|/]"), " ")
        title = title.replace(Regex("\\s+"), " ").trim()

        if (title.length <= 100) return title

        var cut = title.take(100)
        val lastSpace = cut.lastIndexOf(' ')
        if (lastSpace > 80) {
            cut = cut.substring(0, lastSpace)
        }

        return cut.trim { !it.isLetterOrDigit() }
    }

    private fun formatDescription(raw: String?, minChars: Int = 28, maxChars: Int = 170): String {
        if (raw.isNullOrBlank()) return ""

        var text = raw.replace(Regex("[\\n\\r\\t]+"), " ").trim()
        text = text.replace(Regex("[,;:]"), ".")
        text = text.replace(Regex("\\.+"), ".")
        text = text.replace(Regex("\\s+"), " ").trim()

        if (text.isBlank()) return ""

        val terminators = listOf('.', '!', '?')
        val sentences = text.split(Regex("(?<=[.!?])\\s+"))
            .map { it.trim() }
            .filter { it.isNotBlank() }
            .map { s ->
                if (s.last() !in terminators) "$s." else s
            }

        val result = StringBuilder()
        for (sentence in sentences) {
            val next = if (result.isEmpty()) sentence else "$result $sentence"
            if (next.length > maxChars) {
                if (result.isEmpty()) {
                    val cut = next.take(maxChars).trim()
                    return if (cut.isNotEmpty() && cut.last() !in terminators) "$cut." else cut
                }
                break
            }
            result.clear()
            result.append(next)
            if (result.length >= minChars) break
        }

        return result.toString().trim()
    }

    private fun loadThumbnail(url: String) {
        Thread {
            var success = false

            repeat(2) { attempt ->
                try {
                    URL(url).openStream().use { stream ->
                        val bitmap = BitmapFactory.decodeStream(stream)
                        if (bitmap != null) {
                            runOnUiThread {
                                previewImage.setImageBitmap(bitmap)
                            }
                            success = true
                            return@repeat
                        }
                    }
                } catch (e: Exception) {
                    AppLog.write(this, "W", "Не удалось загрузить превью, попытка ${attempt + 1}: ${e.message}", "Thumbnail")
                }

                if (!success && attempt == 0) {
                    try {
                        Thread.sleep(500)
                    } catch (_: Exception) {
                    }
                }
            }

            if (!success) {
                runOnUiThread {
                    previewImage.setImageResource(android.R.drawable.ic_media_play)
                }
                AppLog.write(this, "W", "Превью не загрузилось даже после повторной попытки", "Thumbnail")
            }
        }.start()
    }

    private fun applyAvailableQualities(maxHeight: Int?) {
        currentVideoQualities = if (maxHeight == null) {
            allVideoQualities
        } else {
            allVideoQualities.filter { it.height <= maxHeight }
        }

        val labels = currentVideoQualities.map { it.label }
        val adapter = ArrayAdapter(this, android.R.layout.simple_spinner_item, labels)
        adapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item)
        qualityOrBitrateSpinner.adapter = adapter
        qualityOrBitrateSpinner.setSelection(0)
    }

    private fun applyAudioTrackLabels(tracks: List<String>) {
        // Преобразуем технические названия в красивый формат
        val formattedTracks = tracks.map { rawTrack ->
            formatAudioTrackName(rawTrack)
        }

        currentAudioTracks = if (formattedTracks.isEmpty()) listOf("Авто") else formattedTracks

        val adapter = ArrayAdapter(this, android.R.layout.simple_spinner_item, currentAudioTracks)
        adapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item)
        audioSpinner.adapter = adapter

        // Ищем индекс для авто-выбора "Оригинал"
        val preferredIndex = currentAudioTracks.indexOfFirst {
            it.contains("Оригинал", ignoreCase = true) || it.contains("Original", ignoreCase = true)
        }
        audioSpinner.setSelection(if (preferredIndex >= 0) preferredIndex else 0)

        val showAudio = currentAudioTracks.size > 1
        audioLabel.visibility = if (showAudio) View.VISIBLE else View.GONE
        audioSpinner.visibility = if (showAudio) View.VISIBLE else View.GONE
    }

    /**
     * Вспомогательная функция для форматирования строк типа "Russian (original)" -> "Russian ▪ Оригинал"
     */

    private fun formatAudioTrackName(raw: String): String {
        if (raw.isBlank()) return raw

        // Регулярное выражение для поиска текста вне и внутри скобок: "Язык (Инфо)"
        val regex = Regex("""(.+?)\s*\((.+?)\)""")
        val match = regex.find(raw)

        return if (match != null) {
            var language = match.groupValues[1].trim()
            val extraInfo = match.groupValues[2].trim()

            // 1. Обработка диалектов и письменности (например, Китайский)
            val isSimplified = extraInfo.contains("Simplified", ignoreCase = true) || extraInfo.contains("упрощ", ignoreCase = true)
            val isTraditional = extraInfo.contains("Traditional", ignoreCase = true) || extraInfo.contains("традиц", ignoreCase = true)

            if (isSimplified) language = "$language (Упрощенный)"
            if (isTraditional) language = "$language (Традиционный)"

            // 2. Определение типа: Оригинал или Дубляж
            val isOriginal = extraInfo.contains("original", ignoreCase = true) || extraInfo.contains("оригин", ignoreCase = true)

            // Если в скобках написано то же самое, что и в языке, или просто технический код — это Дубляж
            val type = if (isOriginal) "Оригинал" else "Дубляж"

            "$language ▪ $type"
        } else {
            // Если скобок нет, по умолчанию считаем Оригиналом
            "$raw ▪ Оригинал"
        }
    }

    private fun ensureNotificationPermission() {
        if (Build.VERSION.SDK_INT >= 33 &&
            ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED
        ) {
            ActivityCompat.requestPermissions(this, arrayOf(Manifest.permission.POST_NOTIFICATIONS), 1001)
        }
    }
}
