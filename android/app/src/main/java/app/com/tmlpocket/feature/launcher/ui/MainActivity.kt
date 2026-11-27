package app.com.tmlpocket.feature.launcher.ui

import android.Manifest
import android.animation.ValueAnimator
import android.annotation.SuppressLint
import android.app.Activity
import android.app.ActivityOptions
import android.app.AlertDialog
import android.content.Intent
import android.content.pm.ActivityInfo
import android.content.res.Configuration
import android.graphics.Color
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.provider.Settings
import android.util.Log
import android.view.View
import android.view.WindowInsets
import android.view.WindowInsetsController
import android.webkit.WebChromeClient
import android.webkit.WebResourceError
import android.webkit.WebResourceRequest
import android.webkit.WebResourceResponse
import android.webkit.WebSettings
import android.webkit.WebView
import android.webkit.WebViewClient
import androidx.webkit.JavaScriptReplyProxy
import androidx.webkit.WebViewAssetLoader
import androidx.webkit.WebViewCompat
import androidx.webkit.WebViewFeature
import app.com.tmlpocket.BuildConfig
import app.com.tmlpocket.R
import app.com.tmlpocket.feature.files.FileBrowserAccess
import app.com.tmlpocket.feature.game.GameActivity
import app.com.tmlpocket.feature.game.GameCrashReports
import app.com.tmlpocket.feature.launcher.LauncherController
import app.com.tmlpocket.feature.launcher.MobileControlsSettings
import app.com.tmlpocket.feature.mods.ModRepository.Companion.strings
import app.com.tmlpocket.feature.stores.GogLoginActivity
import org.json.JSONObject

/** Hosts the approved offline UI; privileged operations only accept our main origin. */
class MainActivity : Activity() {
    private lateinit var controller: LauncherController
    private lateinit var web: WebView
    private var channel: JavaScriptReplyProxy? = null
    private var gogRequestId: String? = null
    private var textureRequestId: String? = null
    private val mobileControls by lazy { MobileControlsSettings(java.io.File(filesDir, "userdata")) }
    private var launchPending = false
    private var returningFromGame = false
    private val crashWorker = java.util.concurrent.Executors.newSingleThreadExecutor()
    private var crashCheckPending = false
    private var crashDialog: AlertDialog? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        Log.i("tMLPocketLauncher", "Launcher created; version=${BuildConfig.VERSION_NAME}; pid=${android.os.Process.myPid()}")
        controller =
            (lastNonConfigurationInstance as? LauncherController)
                ?: LauncherController(applicationContext)
        applyOrientation()
        buildUi()
        enterImmersiveMode()
        if (android.os.Build.VERSION.SDK_INT >= 33) {
            onBackInvokedDispatcher.registerOnBackInvokedCallback(
                android.window.OnBackInvokedDispatcher.PRIORITY_DEFAULT
            ) {
                handleBack()
            }
        }
    }

    override fun onStart() {
        super.onStart()
        controller.observer = { snapshot ->
            send(JSONObject().put("event", "snapshot").put("data", snapshot))
        }
    }

    override fun onResume() {
        super.onResume()
        Log.i("tMLPocketLauncher", "Launcher resumed")
        enterImmersiveMode()
        controller.resumeStores()
        controller.refreshRendererCatalog()
        checkGameCrash()
        if (returningFromGame) {
            returningFromGame = false
            web.evaluateJavascript(
                "window.dispatchEvent(new Event('launcher:game-returned'))",
                null,
            )
        }
        if (::web.isInitialized)
            web.evaluateJavascript("window.dispatchEvent(new Event('launcher:files-resume'))", null)
        publishSystemAppearance()
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (hasFocus) {
            enterImmersiveMode()
            checkGameCrash()
        }
    }

    override fun onPause() {
        Log.i("tMLPocketLauncher", "Launcher paused")
        super.onPause()
    }

    private fun checkGameCrash(retries: Int = 3) {
        if (isDestroyed || isFinishing || crashCheckPending || crashDialog != null) return
        crashCheckPending = true
        crashWorker.execute {
            // ActivityManager publishes native exit details asynchronously.
            Thread.sleep(1500)
            val report = runCatching { GameCrashReports.collect(applicationContext) }
                .onFailure { Log.e("tMLPocketLauncher", "Could not collect game crash report", it) }
                .getOrNull()
            runOnUiThread {
                crashCheckPending = false
                if (isDestroyed || isFinishing || !hasWindowFocus()) return@runOnUiThread
                if (report == null) {
                    if (retries > 0) window.decorView.postDelayed({ checkGameCrash(retries - 1) }, 1500)
                    return@runOnUiThread
                }
                val text = android.widget.TextView(this).apply {
                    setText(report.text)
                    setTextIsSelectable(true)
                    textSize = 12f
                    val inset = (16 * resources.displayMetrics.density).toInt()
                    setPadding(inset, inset, inset, inset)
                }
                val scroll = android.widget.ScrollView(this).apply { addView(text) }
                val strings = controller.settings.localizedContext()
                crashDialog = AlertDialog.Builder(this)
                    .setTitle(strings.getString(R.string.game_crashed))
                    .setView(scroll)
                    .setPositiveButton(strings.getString(R.string.close), null)
                    .setNeutralButton(strings.getString(R.string.copy_report)) { _, _ ->
                        (getSystemService(CLIPBOARD_SERVICE) as android.content.ClipboardManager)
                            .setPrimaryClip(android.content.ClipData.newPlainText(strings.getString(R.string.game_crash_report), report.text))
                    }
                    .create().apply {
                        setOnDismissListener { crashDialog = null }
                        show()
                    }
                runCatching { GameCrashReports.handled(applicationContext, report.session) }
            }
        }
    }

    /** 系统深色状态；WebView 的 prefers-color-scheme 不总是跟随，需要主动告知页面。 */
    private fun systemDark() =
        (resources.configuration.uiMode and Configuration.UI_MODE_NIGHT_MASK) ==
            Configuration.UI_MODE_NIGHT_YES

    override fun onConfigurationChanged(newConfig: Configuration) {
        super.onConfigurationChanged(newConfig)
        publishSystemAppearance()
    }

    private fun publishSystemAppearance() {
        if (!::web.isInitialized) return
        val detail = JSONObject()
            .put("dark", systemDark())
            .put("locale", resources.configuration.locales[0].toLanguageTag())
        web.evaluateJavascript(
            "window.dispatchEvent(new CustomEvent('launcher:system-theme',{detail:$detail}))",
            null,
        )
    }

    private fun launchOptions(params: JSONObject): Bundle? {
        if (
            !controller.settings.read().optBoolean("motion", true) ||
                !params.optBoolean("motion", true) ||
                !ValueAnimator.areAnimatorsEnabled()
        )
            return ActivityOptions.makeCustomAnimation(this, 0, 0).toBundle()
        val origin = params.optJSONObject("transition") ?: return null
        val values =
            listOf("left", "top", "width", "height", "viewportWidth").map { origin.optDouble(it) }
        if (values.any { !it.isFinite() } || values[4] <= 0 || web.width <= 0 || web.height <= 0)
            return null
        val scale = web.width / values[4]
        val left = (values[0] * scale).toInt().coerceIn(0, web.width - 1)
        val top = (values[1] * scale).toInt().coerceIn(0, web.height - 1)
        val width = (values[2] * scale).toInt().coerceIn(1, web.width - left)
        val height = (values[3] * scale).toInt().coerceIn(1, web.height - top)
        return ActivityOptions.makeClipRevealAnimation(web, left, top, width, height).toBundle()
    }

    private fun enterImmersiveMode() {
        if (Build.VERSION.SDK_INT >= 30) {
            window.setDecorFitsSystemWindows(false)
            window.insetsController?.apply {
                hide(WindowInsets.Type.systemBars())
                systemBarsBehavior = WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
            }
        } else {
            @Suppress("DEPRECATION")
            window.decorView.systemUiVisibility =
                View.SYSTEM_UI_FLAG_FULLSCREEN or
                    View.SYSTEM_UI_FLAG_HIDE_NAVIGATION or
                    View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY or
                    View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN or
                    View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION or
                    View.SYSTEM_UI_FLAG_LAYOUT_STABLE
        }
    }

    override fun onStop() {
        controller.observer = null
        super.onStop()
    }

    @Deprecated("Retains workers independently of Activity")
    override fun onRetainNonConfigurationInstance(): Any = controller

    override fun onDestroy() {
        crashDialog?.dismiss()
        crashWorker.shutdown()
        channel = null
        if (::web.isInitialized) {
            web.stopLoading()
            (web.parent as? android.view.ViewGroup)?.removeView(web)
            web.destroy()
        }
        if (!isChangingConfigurations) controller.close()
        super.onDestroy()
    }

    @SuppressLint("SetJavaScriptEnabled")
    private fun buildUi() {
        if (!WebViewFeature.isFeatureSupported(WebViewFeature.WEB_MESSAGE_LISTENER)) {
            val strings = controller.settings.localizedContext()
            AlertDialog.Builder(this)
                .setTitle(strings.getString(R.string.webview_update_title))
                .setMessage(strings.getString(R.string.webview_update_detail))
                .setPositiveButton(strings.getString(R.string.close)) { _, _ -> finish() }
                .show()
            return
        }
        val assets = WebViewAssetLoader.AssetsPathHandler(this)
        val loader =
            WebViewAssetLoader.Builder()
                .addPathHandler("/controls-textures/") { name ->
                    mobileControls.texture(name)?.let { WebResourceResponse("image/png", null, it.inputStream()) }
                }
                .addPathHandler("/") { path -> assets.handle("ui/$path") }
                .build()
        web =
            WebView(this).apply {
                setBackgroundColor(Color.rgb(12, 15, 20))
                settings.javaScriptEnabled = true
                settings.domStorageEnabled = true
                settings.allowFileAccess = false
                settings.allowContentAccess = false
                settings.mixedContentMode = WebSettings.MIXED_CONTENT_NEVER_ALLOW
                settings.setSupportMultipleWindows(false)
                settings.mediaPlaybackRequiresUserGesture = true
                webViewClient =
                    object : WebViewClient() {
                        override fun onPageFinished(view: WebView, url: String) {
                            if (Uri.parse(url).host == HOST)
                                Log.i("tMLPocketLauncher", "Launcher UI loaded")
                        }

                        override fun onReceivedError(
                            view: WebView,
                            request: WebResourceRequest,
                            error: WebResourceError,
                        ) {
                            if (request.isForMainFrame)
                                Log.e("tMLPocketLauncher", "Launcher UI failed to load; code=${error.errorCode}")
                        }

                        override fun onReceivedHttpError(
                            view: WebView,
                            request: WebResourceRequest,
                            response: WebResourceResponse,
                        ) {
                            if (request.isForMainFrame)
                                Log.e("tMLPocketLauncher", "Launcher UI HTTP error; status=${response.statusCode}")
                        }

                        override fun shouldInterceptRequest(
                            view: WebView,
                            request: WebResourceRequest,
                        ): WebResourceResponse? {
                            if (request.url.toString().startsWith("data:image/")) return null
                            val uri = request.url
                            if (!request.isForMainFrame && request.method == "GET" &&
                                uri.scheme == "https" && uri.encodedAuthority == "fireworksky.github.io" &&
                                uri.encodedPath in setOf(
                                    "/tML-Pocket-Launcher/news-zh-CN.json",
                                    "/tML-Pocket-Launcher/news-en.json",
                                )
                            ) return null
                            if (!request.isForMainFrame && request.method == "GET" &&
                                SteamWorkshopPreview.isAllowed(request.url.toString()))
                                return SteamWorkshopPreview.load(request.url.toString())
                                    ?: WebResourceResponse(
                                        "text/plain", "UTF-8", 404, "Not Found", emptyMap(), ByteArray(0).inputStream(),
                                    )
                            if (request.url.scheme != "https" || request.url.host != HOST)
                                return WebResourceResponse(
                                    "text/plain",
                                    "UTF-8",
                                    403,
                                    "Forbidden",
                                    emptyMap(),
                                    "External content blocked".byteInputStream(),
                                )
                            return loader.shouldInterceptRequest(request.url)
                                ?: WebResourceResponse(
                                    "text/plain",
                                    "UTF-8",
                                    404,
                                    "Not Found",
                                    emptyMap(),
                                    ByteArray(0).inputStream(),
                                )
                        }

                        override fun shouldOverrideUrlLoading(
                            view: WebView,
                            request: WebResourceRequest,
                        ): Boolean {
                            if (request.url.scheme == "https" && request.url.host == HOST)
                                return false
                            if (request.isForMainFrame && request.hasGesture())
                                openExternal(request.url.toString())
                            return true
                        }
                    }
                webChromeClient = WebChromeClient()
            }
        WebView.setWebContentsDebuggingEnabled(BuildConfig.DEBUG)
        WebViewCompat.addWebMessageListener(web, "LauncherNative", setOf(ORIGIN)) {
            _,
            message,
            origin,
            mainFrame,
            reply ->
            if (!mainFrame || origin.toString() != ORIGIN) return@addWebMessageListener
            channel = reply
            val text = message.data ?: return@addWebMessageListener
            if (text.length > 256 * 1024) return@addWebMessageListener
            var id: String? = null
            try {
                val request = JSONObject(text)
                id = request.getString("id")
                require(id.length <= 80)
                dispatch(
                    id,
                    request.getString("method"),
                    request.optJSONObject("params") ?: JSONObject(),
                )
            } catch (failure: Exception) {
                id?.let { respond(it, Result.failure(failure)) }
            }
        }
        setContentView(web)
        web.loadUrl("$ORIGIN/index.html?system-theme=" + if (systemDark()) "dark" else "light")
    }

    private fun dispatch(id: String, method: String, params: JSONObject) {
        when (method) {
            "store.gog.authorize" -> {
                require(gogRequestId == null) { "GOG 授权页面已经打开" }
                controller.request(
                    "store.gog.auth",
                    JSONObject().put("state", java.util.UUID.randomUUID().toString()),
                ) { result ->
                    if (isDestroyed || isFinishing) return@request
                    result.fold(
                        { value ->
                            runCatching {
                                    gogRequestId = id
                                    startActivityForResult(
                                        Intent(this, GogLoginActivity::class.java)
                                            .putExtra(
                                                "url",
                                                (value as JSONObject).getString("url"),
                                            ),
                                        GOG_LOGIN,
                                    )
                                }
                                .onFailure {
                                    gogRequestId = null
                                    respond(id, Result.failure(it))
                                }
                        },
                        { respond(id, Result.failure(it)) },
                    )
                }
            }
            "appearance.system" ->
                respond(id, Result.success(JSONObject().put("dark", systemDark())))
            "files.permission" -> {
                if (!FileBrowserAccess(this).granted()) {
                    if (Build.VERSION.SDK_INT >= 30) {
                        val intent =
                            Intent(
                                Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION,
                                Uri.parse("package:$packageName"),
                            )
                        try {
                            startActivity(intent)
                        } catch (_: android.content.ActivityNotFoundException) {
                            startActivity(
                                Intent(Settings.ACTION_MANAGE_ALL_FILES_ACCESS_PERMISSION)
                            )
                        }
                    } else {
                        requestPermissions(
                            arrayOf(
                                Manifest.permission.READ_EXTERNAL_STORAGE,
                                Manifest.permission.WRITE_EXTERNAL_STORAGE,
                            ),
                            FILE_ACCESS,
                        )
                    }
                }
                respond(id, Result.success(true))
            }
            "launch" -> {
                Log.i("tMLPocketLauncher", "Game launch requested")
                if (launchPending || returningFromGame) {
                    respond(id, Result.failure(IllegalStateException("游戏启动正在进行")))
                    return
                }
                launchPending = true
                controller.request("launch.check", JSONObject()) { result ->
                    if (isDestroyed || isFinishing) return@request
                    val launched =
                        result.mapCatching {
                            val args =
                                (it as JSONObject)
                                    .getJSONArray("arguments")
                                    .strings()
                                    .toTypedArray()
                            val crashSession = GameCrashReports.begin(
                                applicationContext,
                                it.optString("version"),
                                controller.settings.read().optString("rendererPlugin", "native"),
                            )
                            startActivity(
                                Intent(this, GameActivity::class.java)
                                    .putExtra("launcherArguments", args)
                                    .putExtra("crashSession", crashSession)
                                    .putExtra("tmlVersion", it.optString("version")),
                                launchOptions(params),
                            )
                            returningFromGame = true
                            Log.i("tMLPocketLauncher", "Game handed off to ${packageName}:game")
                            JSONObject().put("handedOff", true)
                        }
                    launchPending = false
                    respond(id, launched)
                }
            }
            "openExternal" -> {
                openExternal(params.getString("url"))
                respond(id, Result.success(true))
            }
            "controls.editor" -> {
                editingControls = params.getBoolean("active")
                applyOrientation()
                enterImmersiveMode()
                respond(id, Result.success(true))
            }
            "controls.texture.pick" -> {
                check(editingControls && textureRequestId == null) { "贴图选择器不可用" }
                val intent = Intent(Intent.ACTION_OPEN_DOCUMENT)
                    .addCategory(Intent.CATEGORY_OPENABLE)
                    .setType("image/png")
                @Suppress("DEPRECATION")
                startActivityForResult(intent, CONTROL_TEXTURE)
                textureRequestId = id
            }
            "finish" -> {
                respond(id, Result.success(true))
                finish()
            }
            "share" -> {
                val text = params.getString("text")
                require(text.length <= 200_000) { "分享内容过长" }
                startActivity(
                    Intent.createChooser(
                        Intent(Intent.ACTION_SEND)
                            .setType("text/plain")
                            .putExtra(Intent.EXTRA_TEXT, text),
                        controller.settings.localizedContext().getString(R.string.share),
                    )
                )
                respond(id, Result.success(true))
            }
            else ->
                controller.request(method, params) { result ->
                    if (method == "settings.save" && result.isSuccess) applyOrientation()
                    if (method == "store.gog.logout" && result.isSuccess) {
                        android.webkit.CookieManager.getInstance().removeAllCookies(null)
                        android.webkit.CookieManager.getInstance().flush()
                    }
                    respond(id, result)
                }
        }
    }

    private fun applyOrientation() {
        requestedOrientation =
            if (!editingControls && controller.settings.read().optString("orientation", "landscape") == "portrait")
                ActivityInfo.SCREEN_ORIENTATION_SENSOR_PORTRAIT
            else ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE
    }

    private var editingControls = false

    private fun openExternal(address: String) {
        val uri = Uri.parse(address)
        require(uri.scheme == "https" && !uri.host.isNullOrBlank()) { "只能打开 HTTPS 网页" }
        startActivity(Intent(Intent.ACTION_VIEW, uri).addCategory(Intent.CATEGORY_BROWSABLE))
    }

    override fun onRequestPermissionsResult(
        requestCode: Int,
        permissions: Array<out String>,
        grantResults: IntArray,
    ) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        if (requestCode == FILE_ACCESS && ::web.isInitialized)
            web.evaluateJavascript("window.dispatchEvent(new Event('launcher:files-resume'))", null)
    }

    @Deprecated("GOG authorization Activity callback")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (requestCode == CONTROL_TEXTURE) {
            val id = textureRequestId ?: return
            textureRequestId = null
            val uri = data?.data
            if (resultCode != RESULT_OK || uri == null) respond(id, Result.success(JSONObject.NULL))
            else crashWorker.execute {
                val result = runCatching {
                    val hash = contentResolver.openInputStream(uri)?.use(mobileControls::importTexture)
                        ?: error("无法读取控件贴图")
                    JSONObject().put("hash", hash)
                }
                runOnUiThread { respond(id, result) }
            }
            return
        }
        if (requestCode == GOG_LOGIN) {
            val id = gogRequestId
            gogRequestId = null
            if (id != null) {
                val code = data?.getStringExtra("code")
                if (resultCode != RESULT_OK || code == null) respond(id, Result.success(false))
                else
                    controller.request("store.gog.login", JSONObject().put("code", code)) {
                        respond(id, it)
                    }
            }
            return
        }
    }

    private fun respond(id: String, result: Result<Any>) {
        result.exceptionOrNull()?.let {
            Log.e("tMLPocketLauncher", "Launcher request failed; type=${it.javaClass.name}")
        }
        val response = JSONObject().put("id", id).put("ok", result.isSuccess)
        result.fold({ response.put("data", it) }, { response.put("error", it.message ?: "操作失败") })
        send(response)
    }

    private fun send(message: JSONObject) {
        if (!isDestroyed && !isFinishing && ::web.isInitialized)
            runCatching { channel?.postMessage(message.toString()) }
    }

    @Deprecated("Web launcher owns page navigation")
    @SuppressLint("GestureBackNavigation") // Legacy dispatch for API 28–32 only.
    override fun onBackPressed() {
        handleBack()
    }

    private fun handleBack() {
        if (::web.isInitialized)
            web.evaluateJavascript("window.dispatchEvent(new Event('launcher:back'))", null)
        else super.onBackPressed()
    }

    companion object {
        private const val HOST = "appassets.androidplatform.net"
        private const val ORIGIN = "https://appassets.androidplatform.net"
        private const val GOG_LOGIN = 103
        private const val FILE_ACCESS = 104
        private const val CONTROL_TEXTURE = 105
    }
}
