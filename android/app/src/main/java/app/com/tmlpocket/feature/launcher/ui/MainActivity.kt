package app.com.tmlpocket.feature.launcher.ui

import android.Manifest
import android.annotation.SuppressLint
import android.app.Activity
import android.app.ActivityOptions
import android.content.ActivityNotFoundException
import android.content.ClipData
import android.content.ClipboardManager
import android.content.ComponentName
import android.content.Intent
import android.content.pm.ActivityInfo
import android.content.pm.PackageManager
import android.content.res.Configuration
import android.graphics.Color
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.Environment
import android.os.Handler
import android.os.Looper
import android.os.ResultReceiver
import android.provider.Settings
import android.provider.DocumentsContract
import android.text.TextUtils
import android.util.Log
import android.view.View
import android.view.WindowInsets
import android.view.WindowInsetsController
import android.webkit.ValueCallback
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
import app.com.tmlpocket.feature.installation.GameStorage
import app.com.tmlpocket.feature.installation.MobileTerrariaChannel
import app.com.tmlpocket.feature.launcher.LauncherController
import app.com.tmlpocket.feature.launcher.LauncherDiagnostics
import app.com.tmlpocket.feature.launcher.ControlLayoutArchive
import app.com.tmlpocket.feature.launcher.MobileControlsSettings
import app.com.tmlpocket.feature.mods.ModRepository.Companion.strings
import app.com.tmlpocket.feature.stores.GogLoginActivity
import org.json.JSONObject

/** Hosts the approved offline UI; privileged operations only accept our main origin. */
class MainActivity : Activity() {
    private lateinit var controller: LauncherController
    private lateinit var web: WebView
    private var updateRequired = false
    private var channel: JavaScriptReplyProxy? = null
    private var gogRequestId: String? = null
    private var packRequest: Pair<String, String?>? = null
    private var controlPngCallback: ValueCallback<Array<Uri>>? = null
    private val gameStorage by lazy { GameStorage(applicationContext) }
    private var hadFileAccess = false
    private var launchPending = false
    private var returningFromGame = false
    private val crashWorker = java.util.concurrent.Executors.newSingleThreadExecutor()
    private var crashCheckPending = false
    private var pendingCrash: GameCrashReports.Report? = null
    private var crashUiReady = false

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        savedInstanceState?.getString("packRequestId")?.let {
            packRequest = it to savedInstanceState.getString("packExportId")
        }
        Log.i("tMLPocketLauncher", "Launcher created; version=${BuildConfig.VERSION_NAME}; pid=${android.os.Process.myPid()}")
        controller =
            (lastNonConfigurationInstance as? LauncherController)
                ?: LauncherController(applicationContext)
        hadFileAccess = gameStorage.granted
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

    override fun onSaveInstanceState(outState: Bundle) {
        super.onSaveInstanceState(outState)
        packRequest?.let { (id, exportId) ->
            outState.putString("packRequestId", id)
            outState.putString("packExportId", exportId)
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
        if (!::web.isInitialized || updateRequired) return
        if (!controller.settings.hasAcceptedNotice()) {
            publishSystemAppearance()
            return
        }
        resumeLauncher()
    }

    private fun resumeLauncher() {
        controller.launcherResumed()
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
        refreshFileAccess()
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
        if (isDestroyed || isFinishing || !crashUiReady || !hasWindowFocus() ||
            crashCheckPending || pendingCrash != null) return
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
                pendingCrash = report
                publishCrash(report)
            }
        }
    }

    private fun publishCrash(report: GameCrashReports.Report) {
        val data = JSONObject().put("session", report.session).put("text", report.text)
        send(JSONObject().put("event", "crashReport").put("data", data))
    }

    /** 系统深色状态；WebView 的 prefers-color-scheme 不总是跟随，需要主动告知页面。 */
    private fun systemDark() =
        (resources.configuration.uiMode and Configuration.UI_MODE_NIGHT_MASK) ==
            Configuration.UI_MODE_NIGHT_YES

    override fun onConfigurationChanged(newConfig: Configuration) {
        super.onConfigurationChanged(newConfig)
        if (updateRequired) loadWebViewUpdate() else publishSystemAppearance()
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

    private fun launchProgressReceiver() = object : ResultReceiver(Handler(Looper.getMainLooper())) {
        override fun onReceiveResult(resultCode: Int, resultData: Bundle?) {
            val detail = resultData?.getString("detail") ?: return
            if (returningFromGame) send(JSONObject().put("event", "gameStartup").put("data", detail))
        }
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
        controlPngCallback?.onReceiveValue(null)
        controlPngCallback = null
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

    private fun loadWebViewUpdate() {
        val strings = controller.settings.localizedContext()
        val theme = when (controller.settings.read().optString("theme")) {
            "dark", "tml" -> "dark"
            "light" -> "light"
            else -> if (systemDark()) "dark" else "light"
        }
        val html = resources.openRawResource(R.raw.webview_update).bufferedReader().use { it.readText() }
            .replace("{{language}}", strings.resources.configuration.locales[0].toLanguageTag())
            .replace("{{theme}}", theme)
            .replace("{{title}}", TextUtils.htmlEncode(strings.getString(R.string.webview_update_title)))
            .replace("{{detail}}", TextUtils.htmlEncode(strings.getString(R.string.webview_update_detail)))
            .replace("{{close}}", TextUtils.htmlEncode(strings.getString(R.string.close)))
        web.loadDataWithBaseURL(ORIGIN, html, "text/html", "UTF-8", null)
    }

    private fun openWebViewSettings() {
        val provider = runCatching { WebView.getCurrentWebViewPackage()?.packageName }.getOrNull()
        val actions = listOfNotNull(
            provider?.let { Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.parse("package:$it")) },
            Intent(Intent.ACTION_VIEW, Uri.parse("market://details?id=${provider ?: "com.google.android.webview"}")),
            Intent(Settings.ACTION_MANAGE_APPLICATIONS_SETTINGS),
        )
        for (intent in actions) {
            try {
                startActivity(intent)
                break
            } catch (error: ActivityNotFoundException) {
                Log.w("tMLPocketLauncher", "System cannot open WebView settings via ${intent.action}", error)
            } catch (error: SecurityException) {
                Log.w("tMLPocketLauncher", "System restricted WebView settings via ${intent.action}", error)
            }
        }
        finish()
    }

    @SuppressLint("SetJavaScriptEnabled")
    private fun buildUi() {
        val providerView = runCatching { WebView(this) }.getOrElse {
            Log.e("tMLPocketLauncher", "System WebView is unavailable", it)
            openWebViewSettings()
            return
        }
        val supportsBridge = runCatching {
            WebViewFeature.isFeatureSupported(WebViewFeature.WEB_MESSAGE_LISTENER)
        }.getOrDefault(false)
        if (!supportsBridge) {
            web = providerView
            updateRequired = true
            web.settings.apply {
                javaScriptEnabled = false
                allowFileAccess = false
                allowContentAccess = false
                blockNetworkLoads = true
            }
            web.webViewClient = object : WebViewClient() {
                override fun shouldOverrideUrlLoading(view: WebView, request: WebResourceRequest): Boolean {
                    if (request.isForMainFrame && request.url.toString() == "tmlpocket://close") finish()
                    return true
                }
            }
            setContentView(web)
            loadWebViewUpdate()
            return
        }
        val assets = WebViewAssetLoader.AssetsPathHandler(this)
        val loader =
            WebViewAssetLoader.Builder()
                .addPathHandler("/") { path -> assets.handle("ui/$path") }
                .build()
        web =
            providerView.apply {
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
                webChromeClient = object : WebChromeClient() {
                    override fun onShowFileChooser(
                        view: WebView,
                        callback: ValueCallback<Array<Uri>>,
                        params: FileChooserParams,
                    ): Boolean {
                        if (params.mode != FileChooserParams.MODE_OPEN ||
                            params.acceptTypes.singleOrNull() != "image/png" ||
                            Uri.parse(view.url).let { it.scheme != "https" || it.host != HOST }
                        ) return false
                        if (controlPngCallback != null) {
                            callback.onReceiveValue(null)
                            return true
                        }
                        controlPngCallback = callback
                        try {
                            val open = Intent(Intent.ACTION_OPEN_DOCUMENT)
                                .addCategory(Intent.CATEGORY_OPENABLE).setType("image/png")
                            startActivityForResult(open, CONTROL_PNG)
                        } catch (_: ActivityNotFoundException) {
                            controlPngCallback = null
                            callback.onReceiveValue(null)
                        }
                        return true
                    }
                }
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
            if (text.length > MobileControlsSettings.MAX_BYTES + 4096) {
                // The bridge writes the request id first. Reject oversized edits
                // without parsing an unbounded profile or leaving a pending promise.
                BRIDGE_ID.find(text.take(160))?.groupValues?.get(1)?.let {
                    respond(it, Result.failure(IllegalArgumentException("请求内容过大（触控配置上限 2 MiB）")))
                }
                return@addWebMessageListener
            }
            var id: String? = null
            try {
                val request = JSONObject(text)
                id = request.getString("id")
                require(id.length <= 80)
                val method = request.getString("method")
                require(method == "controls.save" || text.length <= 256 * 1024) { "请求内容过大" }
                dispatch(
                    id,
                    method,
                    request.optJSONObject("params") ?: JSONObject(),
                )
            } catch (failure: Exception) {
                id?.let { respond(it, Result.failure(failure)) }
            }
        }
        setContentView(web)
        val theme = if (systemDark()) "dark" else "light"
        web.loadUrl("$ORIGIN/index.html?system-theme=$theme&notice-required=${!controller.settings.hasAcceptedNotice()}")
    }

    private fun dispatch(id: String, method: String, params: JSONObject) {
        if (method == "notice.accept") {
            val wasAccepted = controller.settings.hasAcceptedNotice()
            check(controller.settings.acceptNotice()) { "无法保存同意状态，请重试" }
            respond(id, Result.success(true))
            if (!wasAccepted) resumeLauncher()
            return
        }
        if (!controller.settings.hasAcceptedNotice() && method != "finish") {
            error("请先阅读并同意启动器声明")
        }
        when (method) {
            "system.childProcessLimit" -> {
                val status = if (Build.VERSION.SDK_INT < 31) "unsupported" else {
                    when (runCatching { Settings.Global.getString(contentResolver, "settings_enable_monitor_phantom_procs") }.getOrNull()) {
                        "0", "false" -> "unrestricted"
                        "1", "true" -> "restricted"
                        else -> "unknown"
                    }
                }
                respond(id, Result.success(status))
            }
            "system.developerOptions" -> {
                val direct = try {
                    startActivity(Intent(Settings.ACTION_APPLICATION_DEVELOPMENT_SETTINGS))
                    true
                } catch (_: ActivityNotFoundException) { false }
                  catch (_: SecurityException) { false }
                if (!direct) startActivity(Intent(Settings.ACTION_SETTINGS))
                respond(id, Result.success(direct))
            }
            "crashReport.ready" -> {
                crashUiReady = true
                pendingCrash?.let(::publishCrash) ?: checkGameCrash()
                respond(id, Result.success(true))
            }
            "gameDiagnostics.share", "crashReport.share", "crashReport.shareLatest", "crashReport.shareLauncher" -> {
                val session = if (method == "crashReport.share") requireNotNull(pendingCrash).session.also {
                    require(it == params.getString("session"))
                } else null
                crashWorker.execute {
                    val result = runCatching {
                        if (method == "gameDiagnostics.share") GameCrashReports.shareRun(applicationContext)
                        else if (method == "crashReport.shareLauncher") LauncherDiagnostics.archive(applicationContext)
                        else GameCrashReports.archive(applicationContext,
                            session ?: GameCrashReports.mostRecentReport(applicationContext))
                    }
                    runOnUiThread {
                        if (!isFinishing && !isDestroyed) respond(id, result.mapCatching { uri ->
                            startActivity(GameCrashReports.shareIntent(uri, controller.settings.localizedContext().getString(R.string.share)))
                            true
                        })
                    }
                }
            }
            "crashReport.copy", "crashReport.dismiss" -> {
                val report = pendingCrash
                if (report != null && report.session == params.getString("session")) {
                    if (method == "crashReport.copy") {
                        val label = controller.settings.localizedContext().getString(R.string.game_crash_report)
                        requireNotNull(getSystemService(ClipboardManager::class.java))
                            .setPrimaryClip(ClipData.newPlainText(label, report.text))
                    } else {
                        GameCrashReports.handled(applicationContext, report.session)
                        pendingCrash = null
                    }
                }
                respond(id, Result.success(true))
            }
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
            "storage.openFolder" -> {
                openStorageFolder(params.getString("folder"))
                respond(id, Result.success(true))
            }
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
                            val plan = it as JSONObject
                            if (plan.optBoolean("missingTerrariaPe")) return@mapCatching plan
                            val args =
                                plan.getJSONArray("arguments")
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
                                    .putExtra("launchProgress", launchProgressReceiver())
                                    .putExtra("crashSession", crashSession)
                                    .putExtra("tmlVersion", it.optString("version")),
                                ActivityOptions.makeCustomAnimation(this, 0, 0).toBundle(),
                            )
                            returningFromGame = true
                            controller.gameHandedOff()
                            Log.i("tMLPocketLauncher", "Game handed off to ${packageName}:game")
                            JSONObject().put("handedOff", true)
                        }
                    launchPending = false
                    respond(id, launched)
                }
            }
            "terraria.mobile.openStore" -> {
                openTerrariaStore()
                respond(id, Result.success(true))
            }
            "openExternal" -> {
                openExternal(params.getString("url"))
                respond(id, Result.success(true))
            }
            "controls.packs.import", "controls.packs.export" -> {
                check(packRequest == null) { "文件选择器已经打开" }
                val exportId = if (method.endsWith(".export")) params.getString("id") else null
                val intent = if (exportId == null) Intent(Intent.ACTION_OPEN_DOCUMENT)
                else Intent(Intent.ACTION_CREATE_DOCUMENT).putExtra(Intent.EXTRA_TITLE,
                    ControlLayoutArchive.filename(params.optString("name", "Control Layout")))
                intent.addCategory(Intent.CATEGORY_OPENABLE).type = "application/zip"
                if (exportId == null) {
                    // Prefer the same document browser used for export so the saved ZIP is visible.
                    val saveIntent = Intent(Intent.ACTION_CREATE_DOCUMENT)
                        .addCategory(Intent.CATEGORY_OPENABLE).setType("application/zip")
                    val browser = saveIntent.resolveActivity(packageManager)?.packageName
                    if (browser != null && Intent(intent).setPackage(browser).resolveActivity(packageManager) != null)
                        intent.setPackage(browser)
                    if (Build.VERSION.SDK_INT >= 26)
                        intent.putExtra(DocumentsContract.EXTRA_INITIAL_URI,
                            DocumentsContract.buildDocumentUri("com.android.externalstorage.documents", "primary:Download"))
                }
                startActivityForResult(intent, PACK_DOCUMENT)
                packRequest = id to exportId
            }
            "controls.packs.share" -> controller.controlPackShare(params.getString("id")) { result ->
                if (!isDestroyed && !isFinishing) respond(id, result.mapCatching { uri ->
                    val intent = Intent(Intent.ACTION_SEND).setType("application/zip")
                        .putExtra(Intent.EXTRA_STREAM, uri)
                        .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                    intent.clipData = ClipData.newRawUri(null, uri)
                    startActivity(Intent.createChooser(intent, controller.settings.localizedContext().getString(R.string.share)))
                    JSONObject().put("completed", true)
                })
            }
            "controls.editor" -> {
                editingControls = params.getBoolean("active")
                applyOrientation()
                enterImmersiveMode()
                respond(id, Result.success(true))
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

    private fun openStorageFolder(folder: String) {
        val target = when (folder) {
            "games" -> gameStorage.packageRoot.resolve("games")
            "sharedRoot" -> gameStorage.sharedRoot.takeIf { gameStorage.sharedExists }
            "userdata" -> gameStorage.userData()
            "mods" -> gameStorage.userData().resolve("Mods")
            else -> null
        }
        require(target != null && target.isDirectory) { "目录尚未创建或无法访问" }
        val shared = target.toPath().startsWith(gameStorage.sharedRoot.toPath())
        val authority = if (shared) "com.android.externalstorage.documents" else "$packageName.folders"
        val documentId = if (shared) {
            @Suppress("DEPRECATION")
            "primary:" + target.relativeTo(Environment.getExternalStorageDirectory()).invariantSeparatorsPath
        } else if (folder == "games") "games"
        else "user-" + gameStorage.instances().activeId() + if (folder == "mods") "/Mods" else ""
        val uri = DocumentsContract.buildDocumentUri(authority, documentId)
        val view = Intent(Intent.ACTION_VIEW).setDataAndType(uri, DocumentsContract.Document.MIME_TYPE_DIR)
        if (!shared) {
            val tree = DocumentsContract.buildTreeDocumentUri(authority, documentId)
            view.clipData = ClipData.newRawUri(null, tree).apply { addItem(ClipData.Item(uri)) }
            view.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_GRANT_PREFIX_URI_PERMISSION)
        }
        val folderView = Intent(view).setDataAndType(uri, "resource/folder")
        val alternatives = packageManager.queryIntentActivities(folderView,
            PackageManager.MATCH_DEFAULT_ONLY or PackageManager.GET_RESOLVED_FILTER)
            .filter { it.filter?.typesIterator()?.asSequence()?.contains("resource/folder") == true }
            .map { Intent(folderView).setComponent(ComponentName(it.activityInfo.packageName, it.activityInfo.name)) }
        if (view.resolveActivity(packageManager) != null || alternatives.isNotEmpty()) {
            val chooser = Intent.createChooser(view, null)
            if (alternatives.isNotEmpty()) {
                chooser.putExtra(Intent.EXTRA_INITIAL_INTENTS, alternatives.toTypedArray())
                val folderPackages = alternatives.map { it.component!!.packageName }
                val duplicates = packageManager.queryIntentActivities(view, PackageManager.MATCH_DEFAULT_ONLY)
                    .filter { it.activityInfo.packageName in folderPackages }
                    .map { ComponentName(it.activityInfo.packageName, it.activityInfo.name) }
                    .filter { component -> alternatives.none { it.component == component } }
                chooser.putExtra(Intent.EXTRA_EXCLUDE_COMPONENTS, duplicates.toTypedArray())
            }
            startActivity(chooser)
        } else {
            val browse = Intent(Intent.ACTION_OPEN_DOCUMENT_TREE).putExtra(DocumentsContract.EXTRA_INITIAL_URI, uri)
            try { startActivity(browse) }
            catch (_: ActivityNotFoundException) { error("未找到可打开目录的文件管理器") }
        }
    }

    private fun openTerrariaStore() {
        val listing = "details?id=${MobileTerrariaChannel.GOOGLE_PLAY.packageName}"
        val market = Intent(Intent.ACTION_VIEW, Uri.parse("market://$listing"))
            .setPackage("com.android.vending")
        try {
            startActivity(market)
        } catch (_: ActivityNotFoundException) {
            openExternal("https://play.google.com/store/apps/$listing")
        } catch (_: SecurityException) {
            openExternal("https://play.google.com/store/apps/$listing")
        }
    }

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
        if (requestCode == FILE_ACCESS) refreshFileAccess()
    }

    private fun refreshFileAccess() {
        val granted = gameStorage.granted
        val changed = granted != hadFileAccess
        hadFileAccess = granted
        if (changed) controller.request("refresh", JSONObject()) { publishFileAccess() }
        publishFileAccess()
    }

    private fun publishFileAccess() {
        if (::web.isInitialized && !updateRequired)
            web.evaluateJavascript("window.dispatchEvent(new Event('launcher:files-resume'))", null)
    }

    @Deprecated("GOG authorization Activity callback")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (requestCode == PACK_DOCUMENT) {
            val (id, exportId) = packRequest ?: return
            packRequest = null
            val uri = data?.data
            if (resultCode == RESULT_OK && uri != null) {
                controller.controlPackDocument(uri, exportId) { respond(id, it) }
            } else if (exportId == null) {
                controller.request("controls.packs.list", JSONObject()) { respond(id, it) }
            } else respond(id, Result.success(JSONObject().put("completed", false)))
            return
        }
        if (requestCode == CONTROL_PNG) {
            val callback = controlPngCallback
            controlPngCallback = null
            val uri = data?.data?.takeIf { resultCode == RESULT_OK && it.scheme == "content" }
            callback?.onReceiveValue(uri?.let { arrayOf(it) })
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
        if (::web.isInitialized && !updateRequired && controller.settings.hasAcceptedNotice())
            web.evaluateJavascript("window.dispatchEvent(new Event('launcher:back'))", null)
        else finish()
    }

    companion object {
        private const val HOST = "appassets.androidplatform.net"
        private const val ORIGIN = "https://appassets.androidplatform.net"
        private const val GOG_LOGIN = 103
        private const val FILE_ACCESS = 104
        private const val PACK_DOCUMENT = 108
        private val BRIDGE_ID = Regex("^\\{\"id\":\"([\\w.-]{1,80})\"")
        private const val CONTROL_PNG = 107
    }
}
