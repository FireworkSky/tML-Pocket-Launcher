package app.com.tmlpocket.feature.game

import android.content.pm.ActivityInfo
import android.graphics.Rect
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.os.ResultReceiver
import android.os.Process
import android.util.Log
import android.view.KeyEvent
import android.view.MotionEvent
import android.view.View
import android.view.ViewGroup
import android.view.WindowInsets
import android.view.WindowInsetsController
import android.view.WindowManager
import android.webkit.WebView
import app.com.tmlpocket.R
import app.com.tmlpocket.core.platform.AndroidBridge
import app.com.tmlpocket.core.runtime.GameRuntime
import app.com.tmlpocket.feature.launcher.LauncherSettings
import java.util.concurrent.CountDownLatch
import org.libsdl.app.SDL
import org.libsdl.app.SDLActivity

/** Owns the SDL process; installation and downloads stay in the launcher Activity. */
class GameActivity : SDLActivity() {
    private lateinit var overlay: GameOverlay
    private var toolsOverlay: GameToolsOverlay? = null
    private var failureDismissed: CountDownLatch? = null
    @Volatile private var latestDetail = ""
    @Volatile private var startupVisible = true
    private var crashSession: String? = null
    @Volatile private var failed = false
    private var bridgeAttached = false
    override fun getMainThreadStackSize(): Long = GameRuntime.THREAD_STACK_BYTES
    @Suppress("DEPRECATION")
    private val launchProgress by lazy { intent.getParcelableExtra<ResultReceiver>("launchProgress") }

    override fun getLibraries() =
        arrayOf("SDL3", "SDL2_compat", "FAudio_sdl3", "FNA3D_sdl3", "theorafile", "main")

    override fun onCreate(savedInstanceState: Bundle?) {
        crashSession = intent.getStringExtra("crashSession")
        crashSession?.let { session ->
            runCatching { GameCrashReports.started(this, session) }
                .onFailure { Log.e(TAG, "Could not initialize game crash journal", it) }
            val previous = Thread.getDefaultUncaughtExceptionHandler()
            Thread.setDefaultUncaughtExceptionHandler { thread, failure ->
                GameCrashReports.failed(this, session, failure)
                if (previous != null) previous.uncaughtException(thread, failure)
                else Process.killProcess(Process.myPid())
            }
        }
        // The launcher already owns the default WebView profile in the main process.
        if (!profileConfigured) {
            WebView.setDataDirectorySuffix("game")
            profileConfigured = true
        }
        super.onCreate(savedInstanceState)
        if (Build.VERSION.SDK_INT >= 33) {
            onBackInvokedDispatcher.registerOnBackInvokedCallback(
                android.window.OnBackInvokedDispatcher.PRIORITY_DEFAULT,
                ::handleGameBack,
            )
        }
        requestedOrientation = ActivityInfo.SCREEN_ORIENTATION_LANDSCAPE
        enterImmersiveMode()
        if (mBrokenLibraries) return
        // Present a focusable window before Android waits for the first SDL buffer.
        mSurface.visibility = View.INVISIBLE
        mSurface.addOnLayoutChangeListener { _, _, _, _, _, _, _, _, _ -> updateGameWindowVisibility() }
        if (LauncherSettings(this).read().optBoolean("highRefreshRate", true)) {
            mSurface.holder.addCallback(object : android.view.SurfaceHolder.Callback {
                override fun surfaceCreated(holder: android.view.SurfaceHolder) = requestHighRefresh()
                override fun surfaceChanged(holder: android.view.SurfaceHolder, format: Int, width: Int, height: Int) = requestHighRefresh()
                override fun surfaceDestroyed(holder: android.view.SurfaceHolder) = Unit
            })
            requestHighRefresh()
        }
        createOverlay()
        toolsOverlay = GameToolsOverlay(this).also { addContentView(it, ViewGroup.LayoutParams(-1, -1)) }
        val strings = LauncherSettings(this).localizedContext()
        updateStartupStatus(strings.getString(R.string.launch_prepare), -1)
        AndroidBridge.attach(this)
        bridgeAttached = true
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            window.decorView.setOnApplyWindowInsetsListener { view, insets ->
                if (!overlay.showingTextPrompt) AndroidBridge.postImeVisibility(insets.isVisible(WindowInsets.Type.ime()))
                overlay.updateKeyboardInset(insets.getInsets(WindowInsets.Type.ime()).bottom)
                view.onApplyWindowInsets(insets)
            }
            window.decorView.requestApplyInsets()
        } else {
            val frame = Rect()
            overlay.viewTreeObserver.addOnGlobalLayoutListener {
                if (overlay.showingTextPrompt) {
                    overlay.getWindowVisibleDisplayFrame(frame)
                    overlay.updateKeyboardInset((overlay.rootView.height - frame.bottom).coerceAtLeast(0))
                }
            }
        }
    }

    override fun loadLibraries() {
        for (library in getLibraries()) {
            crashSession?.let { GameCrashReports.stage(this, it, "加载原生组件: $library") }
            try {
                SDL.loadLibrary(library, this)
            } catch (failure: Throwable) {
                failed = true
                crashSession?.let { GameCrashReports.failed(this, it, failure) }
                throw failure
            }
        }
    }

    // SDL's resizable desktop window and managed bridge must not re-enable sensor rotation.
    override fun setRequestedOrientation(requestedOrientation: Int) {
        super.setRequestedOrientation(ActivityInfo.SCREEN_ORIENTATION_LANDSCAPE)
    }

    override fun sendCommand(command: Int, data: Any?): Boolean =
        super.sendCommand(command, if (command == COMMAND_CHANGE_WINDOW_STYLE) 1 else data)

    // SDL enables text events during window creation, before a game text field is focused.
    override fun shouldShowTextInput(): Boolean = AndroidBridge.isImeRequestedVisible() && !(::overlay.isInitialized && overlay.showingTextPrompt)

    fun hideGameKeyboard() {
        sendCommand(COMMAND_TEXTEDIT_HIDE, null)
    }

    override fun onSoftKeyboardBack() {
        // On Android 9 and 10 the fullscreen SDL window has no IME visibility insets.
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.R && shouldShowTextInput()) {
            AndroidBridge.postImeVisibility(false, userDismissal = true)
        }
    }

    fun updateStartupStatus(detail: String, progressPercent: Int) {
        latestDetail = detail
        crashSession?.let { GameCrashReports.stage(this, it, detail) }
        if (!::overlay.isInitialized || isFinishing || isDestroyed) return
        if (!startupVisible) return
        overlay.recordDetail(detail)
        val status = if (progressPercent in 0..99) "$detail ($progressPercent%)" else detail
        launchProgress?.send(0, Bundle().apply { putString("detail", status) })
    }

    fun recordStartupStatus(detail: String) {
        latestDetail = detail
        crashSession?.let { GameCrashReports.stage(this, it, detail) }
    }

    fun hideStartupStatus() {
        crashSession?.let { GameCrashReports.firstFrame(this, it) }
        startupVisible = false
        updateGameWindowVisibility()
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) setTranslucent(false)
    }

    // Move only the SDL surface offscreen while the translucent activity shows launcher progress.
    fun updateGameWindowVisibility() {
        val visible = !startupVisible || failed || (::overlay.isInitialized && overlay.interactive)
        toolsOverlay?.visibility = if (!startupVisible && !failed && !overlay.interactive) View.VISIBLE else View.GONE
        mSurface?.let { it.translationX = if (visible) 0f else it.width.toFloat() }
    }

    fun enterImmersiveMode() {
        window.clearFlags(WindowManager.LayoutParams.FLAG_FORCE_NOT_FULLSCREEN)
        window.addFlags(WindowManager.LayoutParams.FLAG_FULLSCREEN)
        window.attributes = window.attributes.apply {
            layoutInDisplayCutoutMode =
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                    WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_ALWAYS
                } else {
                    WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES
                }
        }
        @Suppress("DEPRECATION")
        window.decorView.systemUiVisibility =
            View.SYSTEM_UI_FLAG_FULLSCREEN or
                View.SYSTEM_UI_FLAG_HIDE_NAVIGATION or
                View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY or
                View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN or
                View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION or
                View.SYSTEM_UI_FLAG_LAYOUT_STABLE
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            window.setDecorFitsSystemWindows(false)
            window.insetsController?.apply {
                hide(WindowInsets.Type.systemBars())
                systemBarsBehavior = WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
            }
        }
    }

    override fun onPause() {
        if (::overlay.isInitialized) overlay.onPause()
        if (bridgeAttached) AndroidBridge.postLifecycle(true)
        super.onPause()
    }

    override fun onResume() {
        super.onResume()
        enterImmersiveMode()
        if (::overlay.isInitialized) overlay.onResume()
        if (bridgeAttached) AndroidBridge.postLifecycle(false)
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (startupVisible) Log.i(TAG, "Startup window focus=$hasFocus alpha=${window.attributes.alpha} flags=0x${window.attributes.flags.toString(16)}")
        if (hasFocus) {
            if (!mBrokenLibraries && mSurface != null) mSurface.visibility = View.VISIBLE
            enterImmersiveMode()
            if (mSurface != null && LauncherSettings(this).read().optBoolean("highRefreshRate", true)) requestHighRefresh()
            if (::overlay.isInitialized && overlay.interactive) overlay.requestFocus()
        }
        if (bridgeAttached) AndroidBridge.postFocus(hasFocus)
    }

    @Suppress("DEPRECATION")
    private fun requestHighRefresh() {
        runCatching {
            val display = windowManager.defaultDisplay
            val current = display.mode
            val best = display.supportedModes.filter { it.physicalWidth == current.physicalWidth &&
                it.physicalHeight == current.physicalHeight }.maxByOrNull { it.refreshRate } ?: return
            val params = window.attributes
            if (params.preferredDisplayModeId != best.modeId || params.preferredRefreshRate != best.refreshRate) {
                params.preferredDisplayModeId = best.modeId
                params.preferredRefreshRate = best.refreshRate
                window.attributes = params
            }
            val surface = mSurface.holder.surface
            if (surface.isValid && Build.VERSION.SDK_INT >= 31)
                surface.setFrameRate(best.refreshRate, android.view.Surface.FRAME_RATE_COMPATIBILITY_DEFAULT, android.view.Surface.CHANGE_FRAME_RATE_ALWAYS)
            else if (surface.isValid && Build.VERSION.SDK_INT >= 30)
                surface.setFrameRate(best.refreshRate, android.view.Surface.FRAME_RATE_COMPATIBILITY_DEFAULT)
            Log.i(TAG, "Game display requested=${best.refreshRate} Hz observed=${display.refreshRate} Hz")
        }.onFailure { Log.w(TAG, "Game refresh rate request failed", it) }
    }

    override fun dispatchTouchEvent(event: MotionEvent): Boolean {
        if (startupVisible && !failed && !(::overlay.isInitialized && overlay.interactive)) return true
        return super.dispatchTouchEvent(event)
    }

    override fun dispatchKeyEvent(event: KeyEvent): Boolean {
        if (event.keyCode == KeyEvent.KEYCODE_BACK &&
            !org.libsdl.app.SDLControllerManager.isDeviceSDLJoystick(event.deviceId)) {
            if (event.action == KeyEvent.ACTION_UP && !event.isCanceled) handleGameBack()
            return true
        }
        if (::overlay.isInitialized && overlay.interactive) {
            return overlay.dispatchKeyEvent(event)
        }
        if (startupVisible && !failed) return true
        if (bridgeAttached) AndroidBridge.postKey(event)
        return super.dispatchKeyEvent(event)
    }

    @Deprecated("Android 13 and later use OnBackInvokedCallback")
    override fun onBackPressed() = handleGameBack()

    // SDL can ask Android to handle Back explicitly.
    override fun superOnBackPressed() = handleGameBack()

    private fun handleGameBack() {
        if (::overlay.isInitialized && overlay.handleBack()) return
        if (toolsOverlay?.closeMenu() == true) return
        if (bridgeAttached && !startupVisible) AndroidBridge.postBack()
    }

    override fun onRequestPermissionsResult(
        requestCode: Int,
        permissions: Array<out String>,
        grantResults: IntArray,
    ) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        AndroidBridge.postPermission(
            requestCode,
            grantResults.firstOrNull() == android.content.pm.PackageManager.PERMISSION_GRANTED,
        )
    }

    override fun main() {
        try {
            crashSession?.let { GameCrashReports.startGameLog(this, it) }
            GameRuntime.launch(
                this,
                getArguments() +
                    (intent.getStringArrayExtra("launcherArguments") ?: emptyArray()),
            )
            check(!startupVisible) { "游戏运行时在显示首帧前退出：$latestDetail" }
            crashSession?.let { GameCrashReports.normalExit(this, it) }
        } catch (failure: Throwable) {
            failed = true
            crashSession?.let { GameCrashReports.failed(this, it, failure) }
            Log.e(TAG, "Game launch failed", failure)
            runCatching {
                val directory = java.io.File(filesDir, "userdata/Logs").apply { mkdirs() }
                java.io
                    .File(directory, "launcher-startup.log")
                    .appendText("${java.util.Date()}\n${failure.stackTraceToString()}\n")
            }
            val dismissed = CountDownLatch(1)
            val detail = failure.message ?: latestDetail
            Handler(Looper.getMainLooper()).post {
                if (isFinishing || isDestroyed) dismissed.countDown()
                else {
                    failureDismissed = dismissed
                    startupVisible = true
                    overlay.failed(detail) {
                        dismissed.countDown()
                        finish()
                    }
                }
            }
            try {
                dismissed.await()
            } catch (_: InterruptedException) {
                Thread.currentThread().interrupt()
            }
        }
    }

    override fun onDestroy() {
        if (!failed) crashSession?.let { GameCrashReports.normalExit(this, it) }
        failureDismissed?.countDown()
        if (::overlay.isInitialized) overlay.release()
        if (bridgeAttached) AndroidBridge.detach(this)
        super.onDestroy()
        // CoreCLR and SDL cannot be initialized twice in this process.
        Handler(Looper.getMainLooper()).postDelayed({ Process.killProcess(Process.myPid()) }, 100)
    }

    private fun createOverlay() {
        overlay = GameOverlay(this, intent.getStringExtra("tmlVersion").orEmpty())
        addContentView(overlay, ViewGroup.LayoutParams(-1, -1))
    }

    override fun showLibraryLoadError(detail: String?) {
        if (!failed) crashSession?.let { GameCrashReports.failed(this, it, IllegalStateException(detail)) }
        failed = true
        createOverlay()
        overlay.failed(detail.orEmpty()) { finish() }
    }

    fun overlayUnavailable(detail: String) {
        if (!failed) crashSession?.let { GameCrashReports.failed(this, it, IllegalStateException(detail)) }
        failed = true
        finish()
    }

    fun restoreGameFocus() {
        mSurface?.requestFocus()
    }

    fun showTextPrompt(title: String, password: Boolean, result: (String?) -> Unit) {
        if (::overlay.isInitialized) overlay.askForText(title, password, result) else result(null)
    }

    fun showNotice(message: String, long: Boolean) = showGameNotice(message, if (long) 1 else 0, -1, 0, 0)

    override fun showGameNotice(message: String, duration: Int, gravity: Int, xOffset: Int, yOffset: Int) {
        if (::overlay.isInitialized && !isFinishing && !isDestroyed) {
            overlay.showNotice(message, duration != 0, gravity, xOffset, yOffset)
        }
    }

    override fun messageboxShowMessageBox(
        flags: Int, title: String?, message: String?, buttonFlags: IntArray, buttonIds: IntArray,
        buttonTexts: Array<String>, colors: IntArray?,
    ): Int {
        if (buttonIds.isEmpty() || buttonIds.size != buttonFlags.size || buttonIds.size != buttonTexts.size ||
            Looper.myLooper() == Looper.getMainLooper()) return -1
        val chosen = CountDownLatch(1)
        var selection = -1
        runOnUiThread {
            if (!::overlay.isInitialized || isFinishing || isDestroyed) chosen.countDown()
            else overlay.choose(title.orEmpty(), message.orEmpty(), buttonTexts, buttonFlags, colors) { index ->
                selection = buttonIds.getOrElse(index) { -1 }
                chosen.countDown()
            }
        }
        return try {
            chosen.await()
            selection
        } catch (_: InterruptedException) {
            Thread.currentThread().interrupt()
            -1
        }
    }

    companion object {
        private var profileConfigured = false
        private const val TAG = "tMLPocketCoreClr"
    }
}
