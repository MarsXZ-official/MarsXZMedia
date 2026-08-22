package com.marsxz.marsxzmedia

import android.graphics.Typeface
import android.os.Bundle
import android.text.TextUtils
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.Button
import android.widget.LinearLayout
import android.widget.TextView
import androidx.core.content.ContextCompat
import androidx.core.view.setPadding
import androidx.fragment.app.Fragment
import com.marsxz.marsxzmedia.R
import java.text.SimpleDateFormat
import android.graphics.Color
import android.text.SpannableStringBuilder
import android.text.Spanned
import android.text.style.ForegroundColorSpan
import java.util.Date
import java.util.Locale

class HistoryFragment : Fragment() {

    var onEntrySelected: ((HistoryEntry) -> Unit)? = null

    private lateinit var container: LinearLayout

    override fun onCreateView(
        inflater: LayoutInflater,
        parent: ViewGroup?,
        savedInstanceState: Bundle?
    ): View {
        return inflater.inflate(R.layout.fragment_history, parent, false)
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        super.onViewCreated(view, savedInstanceState)
        container = view.findViewById(R.id.historyContainer)
        refreshView()
    }

    fun refreshView() {
        if (!isAdded) return

        container.removeAllViews()
        val entries = HistoryStore.loadAll(requireContext())

        if (entries.isEmpty()) {
            val empty = TextView(requireContext()).apply {
                text = "История пуста"
                textSize = 16f
                gravity = android.view.Gravity.CENTER
                setPadding(dp(16), dp(32), dp(16), dp(16))
            }
            container.addView(empty)
            return
        }

        val sections = entries.groupBy { startOfDay(it.timestamp) }
            .toSortedMap(compareByDescending { it })

        sections.forEach { (day, dayEntries) ->
            container.addView(createSectionTitle(formatSectionTitle(day)))

            val byUrl = dayEntries.groupBy { it.url }
                .toList()
                .sortedByDescending { (_, list) -> list.maxOf { it.timestamp } }

            byUrl.forEach { (_, itemsRaw) ->
                val items = itemsRaw.sortedByDescending { it.timestamp }
                val wrapper = LinearLayout(requireContext()).apply {
                    orientation = LinearLayout.VERTICAL
                    background = ContextCompat.getDrawable(context, R.drawable.history_item_bg)
                    val lp = LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT)
                    lp.setMargins(0, dp(4), 0, dp(4))
                    layoutParams = lp
                }

                if (items.size == 1) {
                    wrapper.addView(createEntryButton(items[0], compact = false))
                } else {
                    wrapper.addView(createGroupPanel(items))
                }
                container.addView(wrapper)
            }
        }
    }

    private fun createSectionTitle(textValue: String): View {
        return TextView(requireContext()).apply {
            text = textValue
            textSize = 18f
            setTypeface(typeface, Typeface.BOLD)
            setTextColor(ContextCompat.getColor(context, R.color.text_primary))
            setPadding(dp(4), dp(12), dp(4), dp(8))
        }
    }

    private fun createGroupPanel(items: List<HistoryEntry>): View {
        val latest = items.first()

        val groupRoot = LinearLayout(requireContext()).apply {
            orientation = LinearLayout.VERTICAL
        }

        val headerButton = Button(requireContext()).apply {
            isAllCaps = false
            gravity = android.view.Gravity.START or android.view.Gravity.CENTER_VERTICAL
            text = buildEntryText(latest.title, latest.timestamp, "▸ ")
            setTextColor(ContextCompat.getColor(context, R.color.text_primary))
            setPadding(dp(12), dp(10), dp(12), dp(10))
            ellipsize = TextUtils.TruncateAt.END
            maxLines = 2
            background = null
        }

        val childrenContainer = LinearLayout(requireContext()).apply {
            orientation = LinearLayout.VERTICAL
            visibility = View.GONE
            setPadding(dp(20), dp(4), dp(0), dp(8))
        }

        items.forEach { item ->
            childrenContainer.addView(createEntryButton(item, compact = true))
        }

        headerButton.setOnClickListener {
            val expanded = childrenContainer.visibility == View.VISIBLE
            childrenContainer.visibility = if (expanded) View.GONE else View.VISIBLE

            headerButton.text = buildEntryText(
                latest.title,
                latest.timestamp,
                if (expanded) "▸ " else "▾ "
            )
        }

        groupRoot.addView(headerButton)
        groupRoot.addView(childrenContainer)
        return groupRoot
    }

    private fun buildEntryText(
        title: String,
        timestamp: Long,
        prefix: String? = null
    ): CharSequence {
        val safeTitle = title.trim()
        val timeText = "[${formatTime(timestamp)}]"

        val fullText = buildString {
            if (!prefix.isNullOrEmpty()) {
                append(prefix)
            }
            append(safeTitle)
            append("  ")
            append(timeText)
        }

        val builder = SpannableStringBuilder(fullText)

        val timeStart = fullText.lastIndexOf(timeText)
        if (timeStart >= 0) {
            builder.setSpan(
                ForegroundColorSpan(Color.parseColor("#8A8A8A")),
                timeStart,
                timeStart + timeText.length,
                Spanned.SPAN_EXCLUSIVE_EXCLUSIVE
            )
        }

        return builder
    }

    private fun createEntryButton(entry: HistoryEntry, compact: Boolean): View {
        return Button(requireContext()).apply {
            isAllCaps = false
            gravity = android.view.Gravity.START or android.view.Gravity.CENTER_VERTICAL
            text = buildEntryText(entry.title, entry.timestamp)
            setTextColor(ContextCompat.getColor(context, R.color.text_primary))
            textSize = if (compact) 14f else 16f
            setPadding(dp(12), dp(10), dp(12), dp(10))
            ellipsize = TextUtils.TruncateAt.END
            maxLines = 2
            background = null

            setOnClickListener {
                onEntrySelected?.invoke(entry)
            }
        }
    }

    private fun formatSectionTitle(dayStartMillis: Long): String {
        val today = startOfDay(System.currentTimeMillis())
        val yesterday = today - 24L * 60L * 60L * 1000L

        return when (dayStartMillis) {
            today -> "Сегодня"
            yesterday -> "Вчера"
            else -> SimpleDateFormat("dd MMM yyyy", Locale.getDefault()).format(Date(dayStartMillis))
        }
    }

    private fun formatTime(timestamp: Long): String {
        return SimpleDateFormat("HH:mm", Locale.getDefault()).format(Date(timestamp))
    }

    private fun startOfDay(timestamp: Long): Long {
        val cal = java.util.Calendar.getInstance().apply {
            timeInMillis = timestamp
            set(java.util.Calendar.HOUR_OF_DAY, 0)
            set(java.util.Calendar.MINUTE, 0)
            set(java.util.Calendar.SECOND, 0)
            set(java.util.Calendar.MILLISECOND, 0)
        }
        return cal.timeInMillis
    }

    private fun dp(value: Int): Int {
        return (value * resources.displayMetrics.density).toInt()
    }
}