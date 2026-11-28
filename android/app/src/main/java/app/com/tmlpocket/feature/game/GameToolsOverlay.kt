package app.com.tmlpocket.feature.game

import android.content.Context
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.view.Gravity
import android.view.MotionEvent
import android.view.View
import android.view.ViewConfiguration
import android.widget.FrameLayout
import android.widget.ImageButton
import android.widget.LinearLayout
import android.widget.TextView
import app.com.tmlpocket.R
import app.com.tmlpocket.core.platform.AndroidBridge
import app.com.tmlpocket.feature.launcher.LauncherSettings
import kotlin.math.hypot
import kotlin.math.roundToInt

/** Only the ball and menu buttons accept touches, leaving the game surface interactive. */
internal class GameToolsOverlay(private val activity: GameActivity) : FrameLayout(activity) {
    private val strings = LauncherSettings(activity).localizedContext()
    private val preferences = activity.getSharedPreferences("game-tools", Context.MODE_PRIVATE)
    private var horizontal = preferences.getFloat("x", 1f)
    private var vertical = preferences.getFloat("y", .25f)
    private val ball = ImageButton(context)
    private val handle = FrameLayout(context)
    private var tucked = true
    private var edge = preferences.getInt("edge", if (horizontal < .5f) Gravity.LEFT else Gravity.RIGHT)
    private val stripe = View(context)
    private val menu = LinearLayout(context)
    private val fps = TextView(context)
    private val fpsButton: TextView
    private val shareButton: TextView
    private var showFps = preferences.getBoolean("fps", false)
    private val refreshFps = object : Runnable {
        override fun run() {
            fps.text = strings.getString(R.string.game_tools_fps_value, AndroidBridge.recentFrameRate())
            postDelayed(this, 1000)
        }
    }

    init {
        isFocusable = false
        visibility = GONE
        ball.apply {
            setImageResource(R.drawable.pocket_launcher_foreground)
            scaleType = android.widget.ImageView.ScaleType.FIT_CENTER
            background = null
            setPadding(0, 0, 0, 0)
            contentDescription = strings.getString(R.string.game_tools)
            isFocusableInTouchMode = false
            setOnClickListener { menu.visibility = if (menu.visibility == VISIBLE) GONE else VISIBLE }
        }
        addView(ball, LayoutParams(dp(36), dp(36)))
        enableDragging()
        stripe.apply {
            background = GradientDrawable().apply {
                setColor(0x66ffffff)
                setStroke(dp(1), 0x66000000)
                cornerRadius = dp(3).toFloat()
            }
        }
        handle.addView(stripe, LayoutParams(dp(5), dp(30), Gravity.CENTER))
        handle.contentDescription = strings.getString(R.string.game_tools_restore)
        handle.setOnClickListener { setTucked(false) }
        addView(handle, LayoutParams(dp(20), dp(48)))
        menu.orientation = LinearLayout.VERTICAL
        menu.background = panelBackground()
        menu.visibility = GONE
        addView(menu, LayoutParams(LayoutParams.WRAP_CONTENT, LayoutParams.WRAP_CONTENT))
        addAction(R.string.game_tools_keyboard) { AndroidBridge.postKeyboardRequest() }
        fpsButton = addAction(fpsActionLabel()) {
            showFps = !showFps
            preferences.edit().putBoolean("fps", showFps).apply()
            updateFpsVisibility()
        }
        shareButton = addAction(R.string.game_tools_share) { shareDiagnostics() }
        addAction(R.string.game_tools_tuck) { setTucked(true) }
        fps.apply {
            gravity = Gravity.CENTER
            setTextColor(Color.WHITE)
            typeface = Typeface.MONOSPACE
            textSize = 12f
            setPadding(dp(6), dp(2), dp(6), dp(2))
            background = panelBackground()
        }
        addView(fps, LayoutParams(LayoutParams.WRAP_CONTENT, LayoutParams.WRAP_CONTENT))
        applyTuckedState()
    }

    private fun setTucked(value: Boolean) {
        if (value) {
            val left = ball.left - leftLimit
            val right = rightLimit - ball.right
            val top = ball.top - topLimit
            val bottom = bottomLimit - ball.bottom
            edge = when (minOf(left, right, top, bottom)) {
                left -> Gravity.LEFT
                right -> Gravity.RIGHT
                top -> Gravity.TOP
                else -> Gravity.BOTTOM
            }
        }
        tucked = value
        if (value) preferences.edit().putInt("edge", edge).apply()
        applyTuckedState()
    }

    private fun applyTuckedState() {
        if (tucked) {
            val alongTopOrBottom = edge == Gravity.TOP || edge == Gravity.BOTTOM
            handle.layoutParams = if (alongTopOrBottom) LayoutParams(dp(48), dp(20)) else LayoutParams(dp(20), dp(48))
            stripe.layoutParams = if (alongTopOrBottom) LayoutParams(dp(30), dp(5), Gravity.CENTER)
                else LayoutParams(dp(5), dp(30), Gravity.CENTER)
        }
        ball.visibility = if (tucked) GONE else VISIBLE
        handle.visibility = if (tucked) VISIBLE else GONE
        updateFpsVisibility()
    }

    private fun dp(value: Int) = (value * resources.displayMetrics.density).roundToInt()

    private fun panelBackground() = GradientDrawable().apply {
        setColor(0xed21232a.toInt())
        cornerRadius = dp(10).toFloat()
    }

    private fun addAction(label: Int, action: () -> Unit): TextView = TextView(context).apply {
        text = strings.getString(label)
        textSize = 14f
        gravity = Gravity.CENTER_VERTICAL
        setTextColor(Color.WHITE)
        setPadding(dp(16), 0, dp(16), 0)
        minHeight = dp(48)
        setOnClickListener { closeMenu(); action() }
        menu.addView(this, LinearLayout.LayoutParams(LayoutParams.MATCH_PARENT, LayoutParams.WRAP_CONTENT))
    }

    fun closeMenu(): Boolean {
        if (menu.visibility != VISIBLE) return false
        menu.visibility = GONE
        return true
    }

    private fun fpsActionLabel() = if (showFps) R.string.game_tools_hide_fps else R.string.game_tools_show_fps

    private fun updateFpsVisibility() {
        fpsButton.text = strings.getString(fpsActionLabel())
        fps.visibility = if (showFps && !tucked) VISIBLE else GONE
        removeCallbacks(refreshFps)
        if (fps.visibility == VISIBLE && isShown && windowVisibility == VISIBLE) refreshFps.run()
    }

    override fun onVisibilityAggregated(isVisible: Boolean) {
        super.onVisibilityAggregated(isVisible)
        if (isVisible) updateFpsVisibility() else {
            removeCallbacks(refreshFps)
            closeMenu()
        }
    }

    private fun enableDragging() {
        val slop = ViewConfiguration.get(context).scaledTouchSlop
        var downX = 0f
        var downY = 0f
        var originX = 0
        var originY = 0
        var dragging = false
        ball.setOnTouchListener { _, event ->
            when (event.actionMasked) {
                MotionEvent.ACTION_DOWN -> {
                    downX = event.rawX
                    downY = event.rawY
                    originX = ball.left
                    originY = ball.top
                    dragging = false
                }
                MotionEvent.ACTION_MOVE -> {
                    val dx = event.rawX - downX
                    val dy = event.rawY - downY
                    if (!dragging && hypot(dx, dy) > slop) {
                        dragging = true
                        closeMenu()
                    }
                    if (dragging) {
                        horizontal = ((originX + dx - leftLimit) / travelX.coerceAtLeast(1)).coerceIn(0f, 1f)
                        vertical = ((originY + dy - topLimit) / travelY.coerceAtLeast(1)).coerceIn(0f, 1f)
                        requestLayout()
                    }
                }
                MotionEvent.ACTION_UP -> if (dragging) {
                    preferences.edit().putFloat("x", horizontal).putFloat("y", vertical).apply()
                } else ball.performClick()
            }
            true
        }
    }

    private val leftLimit get() = dp(6) + (rootWindowInsets?.displayCutout?.safeInsetLeft ?: 0)
    private val topLimit get() = dp(6) + (rootWindowInsets?.displayCutout?.safeInsetTop ?: 0)
    private val rightLimit get() = width - dp(6) - (rootWindowInsets?.displayCutout?.safeInsetRight ?: 0)
    private val bottomLimit get() = height - dp(6) - (rootWindowInsets?.displayCutout?.safeInsetBottom ?: 0)
    private val travelX get() = (rightLimit - leftLimit - ball.measuredWidth).coerceAtLeast(0)
    private val travelY get() = (bottomLimit - topLimit - ball.measuredHeight).coerceAtLeast(0)

    override fun onLayout(changed: Boolean, l: Int, t: Int, r: Int, b: Int) {
        fun place(view: View, x: Int, y: Int) {
            val left = x.coerceIn(leftLimit, (rightLimit - view.measuredWidth).coerceAtLeast(leftLimit))
            val top = y.coerceIn(topLimit, (bottomLimit - view.measuredHeight).coerceAtLeast(topLimit))
            view.layout(left, top, left + view.measuredWidth, top + view.measuredHeight)
        }
        if (tucked) {
            val x = when (edge) {
                Gravity.LEFT -> leftLimit
                Gravity.RIGHT -> rightLimit - handle.measuredWidth
                else -> leftLimit + ((rightLimit - leftLimit - handle.measuredWidth) * horizontal).roundToInt()
            }
            val y = when (edge) {
                Gravity.TOP -> topLimit
                Gravity.BOTTOM -> bottomLimit - handle.measuredHeight
                else -> topLimit + ((bottomLimit - topLimit - handle.measuredHeight) * vertical).roundToInt()
            }
            place(handle, x, y)
            return
        }
        place(ball, leftLimit + (travelX * horizontal).roundToInt(), topLimit + (travelY * vertical).roundToInt())
        val gap = dp(4)
        if (menu.visibility == VISIBLE) {
            val x = if (ball.right + gap + menu.measuredWidth <= rightLimit) ball.right + gap
                    else ball.left - gap - menu.measuredWidth
            place(menu, x, ball.top)
        }
        if (fps.visibility == VISIBLE) {
            val y = if (ball.bottom + gap + fps.measuredHeight <= bottomLimit) ball.bottom + gap
                    else ball.top - gap - fps.measuredHeight
            place(fps, ball.left + (ball.width - fps.measuredWidth) / 2, y)
        }
    }

    private fun shareDiagnostics() {
        if (!shareButton.isEnabled) return
        shareButton.isEnabled = false
        activity.showNotice(strings.getString(R.string.game_tools_preparing), false)
        kotlin.concurrent.thread(name = "game-diagnostics") {
            val archive = runCatching { GameCrashReports.shareRun(activity.applicationContext) }
            activity.runOnUiThread {
                if (activity.isFinishing || activity.isDestroyed) return@runOnUiThread
                shareButton.isEnabled = true
                archive.mapCatching {
                    activity.startActivity(GameCrashReports.shareIntent(it, strings.getString(R.string.share)))
                }.onFailure {
                    android.util.Log.e("tMLPocketLauncher", "Game diagnostics export failed", it)
                    activity.showNotice(strings.getString(R.string.game_tools_share_failed), true)
                }
            }
        }
    }
}
