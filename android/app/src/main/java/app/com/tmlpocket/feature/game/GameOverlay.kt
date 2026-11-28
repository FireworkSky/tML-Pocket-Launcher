package app.com.tmlpocket.feature.game

import android.animation.ValueAnimator
import android.annotation.SuppressLint
import android.content.res.Configuration
import android.graphics.Color
import android.view.Gravity
import android.view.MotionEvent
import android.view.ViewGroup
import android.view.inputmethod.EditorInfo
import android.view.inputmethod.InputConnection
import android.view.inputmethod.InputMethodManager
import android.webkit.RenderProcessGoneDetail
import android.webkit.WebResourceRequest
import android.webkit.WebResourceResponse
import android.webkit.WebView
import android.webkit.WebViewClient
import androidx.webkit.JavaScriptReplyProxy
import androidx.webkit.WebViewAssetLoader
import androidx.webkit.WebViewCompat
import app.com.tmlpocket.R
import app.com.tmlpocket.feature.launcher.LauncherSettings
import org.json.JSONArray
import org.json.JSONObject

/** Dialogs capture input. A notice alone passes input through to the game. */
@SuppressLint("SetJavaScriptEnabled", "ViewConstructor")
class GameOverlay(private val activity: GameActivity, version: String) : WebView(activity) {
    private val preferences = LauncherSettings(activity)
    private val strings = preferences.localizedContext()
    private val appearance = preferences.read()
    private val motion = appearance.optBoolean("motion", true) && ValueAnimator.areAnimatorsEnabled()
    private val labels = JSONObject().apply {
        put("local", strings.getString(R.string.launch_local))
        put("viewLogs", strings.getString(R.string.launch_view_logs))
        put("logs", strings.getString(R.string.launch_logs))
        put("closeLogs", strings.getString(R.string.launch_close_logs))
        put("returnLabel", strings.getString(R.string.launch_return))
    }
    private val failure = JSONObject().put("version", version)
    private val history = ArrayDeque<String>()
    private var logsVisible = false
    private var failureAction: (() -> Unit)? = null
    private var returnStarted = false
    private var channel: JavaScriptReplyProxy? = null
    private var disposed = false
    private data class Prompt(val data: JSONObject, val complete: (Int, String?) -> Unit)
    private val prompts = ArrayDeque<Prompt>()
    private var nextId = 0L
    private var notice: JSONObject? = null
    private val hideNotice = Runnable {
        notice = null
        render()
    }

    val showingTextPrompt get() = prompts.firstOrNull()?.data?.has("input") == true
    private var keyboardInset = 0

    fun updateKeyboardInset(bottom: Int) {
        if (bottom == keyboardInset) return
        keyboardInset = bottom
        if (showingTextPrompt) render()
    }

    val interactive get() = failureAction != null || prompts.isNotEmpty()

    init {
        setBackgroundColor(Color.TRANSPARENT)
        isHorizontalScrollBarEnabled = false
        isVerticalScrollBarEnabled = false
        settings.javaScriptEnabled = true
        settings.allowContentAccess = false
        settings.allowFileAccess = false
        val assets = WebViewAssetLoader.AssetsPathHandler(context)
        val loader = WebViewAssetLoader.Builder().addPathHandler("/") { assets.handle("ui/$it") }.build()
        webViewClient = object : WebViewClient() {
            override fun shouldOverrideUrlLoading(view: WebView, request: WebResourceRequest) = true

            override fun shouldInterceptRequest(view: WebView, request: WebResourceRequest): WebResourceResponse {
                if (request.method == "GET") loader.shouldInterceptRequest(request.url)?.let { return it }
                return WebResourceResponse("text/plain", "UTF-8", 404, "Not Found", emptyMap(), null)
            }

            override fun onRenderProcessGone(view: WebView, detail: RenderProcessGoneDetail): Boolean {
                release()
                activity.overlayUnavailable("The game overlay WebView renderer stopped.")
                return true
            }
        }
        WebViewCompat.addWebMessageListener(this, "GameOverlayNative", setOf(ORIGIN)) {
            _, message, origin, mainFrame, reply ->
            if (!mainFrame || origin.toString() != ORIGIN || disposed) return@addWebMessageListener
            val action = runCatching { JSONObject(message.data ?: "") }.getOrNull() ?: return@addWebMessageListener
            when (action.optString("action")) {
                "ready" -> {
                    channel = reply
                    render()
                }
                "logs" -> if (failureAction != null && prompts.isEmpty()) {
                    logsVisible = action.optBoolean("open")
                    render()
                }
                "return" -> if (prompts.isEmpty()) returnToLauncher()
                "pick" -> {
                    val prompt = prompts.firstOrNull() ?: return@addWebMessageListener
                    if (prompt.data.getLong("id") != action.optLong("id")) return@addWebMessageListener
                    val index = action.optInt("index", -1)
                    if (index !in 0 until prompt.data.getJSONArray("buttons").length()) return@addWebMessageListener
                    finishPrompt(index, action.optString("text"))
                }
            }
        }
        loadUrl("$ORIGIN/game.html")
    }

    fun recordDetail(detail: String) {
        if (history.lastOrNull() != detail) {
            history.addLast(detail.take(8000))
            if (history.size > 30) history.removeFirst()
        }
    }

    fun failed(message: String, returned: () -> Unit) {
        animate().cancel()
        alpha = 1f
        failureAction = returned
        failure.put("title", strings.getString(R.string.launch_failed))
        failure.put("detail", message.take(1000))
        failure.put("note", strings.getString(R.string.launch_check_logs))
        history.addLast(message.take(16000))
        render()
    }

    fun askForText(title: String, password: Boolean, result: (String?) -> Unit) {
        val choices = arrayOf(strings.getString(android.R.string.cancel), strings.getString(android.R.string.ok))
        val data = JSONObject().put("title", title).put("input", if (password) "password" else "text")
            .put("buttons", JSONArray(choices.toList())).put("enter", 1).put("escape", 0)
        enqueue(data) { index, text -> result(if (index == 1) text.orEmpty() else null) }
    }

    fun choose(title: String, message: String, choices: Array<String>, flags: IntArray, colors: IntArray?, result: (Int) -> Unit) {
        val data = JSONObject().put("title", title).put("message", message).put("buttons", JSONArray(choices.toList()))
            .put("enter", flags.indexOfLast { it and 1 != 0 }).put("escape", flags.indexOfLast { it and 2 != 0 })
        if (colors?.size == 5) {
            data.put("colors", JSONArray(colors.map { String.format("#%06x", it and 0xffffff) }))
        }
        enqueue(data) { index, _ -> result(index) }
    }

    private fun enqueue(data: JSONObject, complete: (Int, String?) -> Unit) {
        if (disposed || activity.isFinishing || activity.isDestroyed) {
            complete(-1, null)
            return
        }
        prompts.addLast(Prompt(data.put("id", ++nextId), complete))
        render()
    }

    private fun finishPrompt(index: Int, text: String? = null) {
        val prompt = prompts.removeFirst()
        if (prompt.data.has("input")) {
            activity.getSystemService(InputMethodManager::class.java)?.hideSoftInputFromWindow(windowToken, 0)
        }
        render()
        prompt.complete(index, text)
    }

    fun showNotice(message: String, long: Boolean, gravity: Int, x: Int, y: Int) {
        if (disposed) return
        removeCallbacks(hideNotice)
        val position = if (gravity < 0) Gravity.BOTTOM or Gravity.CENTER_HORIZONTAL else Gravity.getAbsoluteGravity(gravity, layoutDirection)
        val density = resources.displayMetrics.density
        notice = JSONObject().put("text", message)
            .put("horizontal", position and Gravity.HORIZONTAL_GRAVITY_MASK)
            .put("vertical", position and Gravity.VERTICAL_GRAVITY_MASK)
            .put("x", x / density).put("y", y / density)
        render()
        postDelayed(hideNotice, if (long) 3500L else 2000L)
    }

    fun handleBack(): Boolean {
        prompts.firstOrNull()?.let {
            val escape = it.data.getInt("escape")
            if (escape >= 0) finishPrompt(escape)
            return true
        }
        if (logsVisible) {
            logsVisible = false
            render()
            return true
        }
        return returnToLauncher()
    }

    private fun returnToLauncher(): Boolean {
        val action = failureAction ?: return false
        if (!returnStarted) {
            returnStarted = true
            action()
        }
        return true
    }

    private fun render() {
        if (disposed) return
        animate().cancel()
        activity.updateGameWindowVisibility()
        val keepFocus = interactive
        val restoreGameFocus = !keepFocus && hasFocus()
        isFocusableInTouchMode = keepFocus
        isFocusable = keepFocus
        visibility = if (keepFocus || notice != null) VISIBLE else GONE
        alpha = 1f
        if (keepFocus && !hasFocus()) requestFocus()
        if (restoreGameFocus) activity.restoreGameFocus()
        val night = resources.configuration.uiMode and Configuration.UI_MODE_NIGHT_MASK == Configuration.UI_MODE_NIGHT_YES
        val theme = when (appearance.optString("theme")) {
            "tml", "dark" -> "dark"
            "light" -> "light"
            else -> if (night) "dark" else "light"
        }
        channel?.postMessage(JSONObject().apply {
            put("theme", theme)
            put("skin", appearance.optString("themeStyle", "tml"))
            put("language", strings.resources.configuration.locales[0].toLanguageTag())
            put("motion", motion)
            put("keyboardInset", if (showingTextPrompt) keyboardInset / resources.displayMetrics.density else 0)
            put("labels", labels)
            if (failureAction != null) put("failure", failure)
            if (logsVisible) put("logs", history.joinToString("\n\n").ifBlank { failure.getString("detail") })
            prompts.firstOrNull()?.let { put("prompt", it.data) }
            notice?.let { put("notice", it) }
        }.toString())
    }

    override fun onCreateInputConnection(outAttrs: EditorInfo): InputConnection? {
        val connection = super.onCreateInputConnection(outAttrs)
        outAttrs.imeOptions = outAttrs.imeOptions or EditorInfo.IME_FLAG_NO_EXTRACT_UI or EditorInfo.IME_FLAG_NO_FULLSCREEN
        return connection
    }

    override fun onConfigurationChanged(newConfig: Configuration) {
        super.onConfigurationChanged(newConfig)
        render()
    }

    override fun dispatchTouchEvent(event: MotionEvent) = interactive && super.dispatchTouchEvent(event)
    override fun dispatchGenericMotionEvent(event: MotionEvent) = interactive && super.dispatchGenericMotionEvent(event)

    fun release() {
        if (disposed) return
        disposed = true
        removeCallbacks(hideNotice)
        animate().cancel()
        while (prompts.isNotEmpty()) prompts.removeFirst().complete(-1, null)
        channel = null
        (parent as? ViewGroup)?.removeView(this)
        destroy()
    }

    companion object {
        private const val ORIGIN = "https://appassets.androidplatform.net"
    }
}
