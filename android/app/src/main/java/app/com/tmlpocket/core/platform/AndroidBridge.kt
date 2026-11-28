package app.com.tmlpocket.core.platform

import android.app.Activity
import android.content.ClipData
import android.content.Context
import android.content.Intent
import android.content.pm.ActivityInfo
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.SystemClock
import android.view.KeyEvent
import app.com.tmlpocket.feature.game.GameActivity
import java.lang.ref.WeakReference
import org.libsdl.app.SDLActivity

/** Android implementation used by the CoreCLR-side JniAndroidBridge. */
object AndroidBridge {
    private const val STATUS_TAG = "tMLPocketStatus"
    private const val EVENT_LIFECYCLE = 0
    private const val EVENT_TEXT = 1
    private const val EVENT_KEY = 2
    private const val EVENT_FOCUS = 4
    private const val EVENT_BACK = 5
    private const val EVENT_PERMISSION = 6
    private const val EVENT_LAUNCHER_ACTION = 7
    private const val EVENT_IME_COMMIT = 8
    private const val EVENT_IME_COMPOSITION = 9
    private const val EVENT_IME_VISIBILITY = 10
    private const val EVENT_KEYBOARD_REQUEST = 11

    @Volatile private var imeRequestedVisible = false
    @Volatile private var frameRate = 0
    @Volatile private var sampledAt = 0L

    @JvmStatic fun reportFrameRate(fps: Int): Int {
        frameRate = fps
        sampledAt = SystemClock.elapsedRealtime()
        return 0
    }

    internal fun recentFrameRate(): Int =
        if (SystemClock.elapsedRealtime() - sampledAt <= 2500) frameRate else 0
    private var lastImeVisible: Boolean? = null

    @Volatile private var activityReference: WeakReference<Activity>? = null

    @JvmStatic
    fun attach(activity: Activity) {
        activityReference = WeakReference(activity)
        imeRequestedVisible = false
        lastImeVisible = null
        NativeCoreClr.nativeRegisterBridgeActivity(activity)
    }

    @JvmStatic
    fun detach(activity: Activity) {
        if (activityReference?.get() === activity) {
            activityReference = null
            imeRequestedVisible = false
            NativeCoreClr.nativeClearBridgeActivity()
        }
    }

    @JvmStatic
    fun showToast(message: String, longDuration: Int): Int {
        val activity = currentActivity() as? GameActivity ?: return -1
        activity.runOnUiThread { activity.showNotice(message, longDuration != 0) }
        return 0
    }

    @JvmStatic
    fun setStartupStatus(title: String, detail: String, progressPercent: Int): Int {
        val activity = currentActivity()
        if (activity == null) {
            android.util.Log.e(STATUS_TAG, "setStartupStatus ignored: no activity")
            return -1
        }
        android.util.Log.i(STATUS_TAG, "setStartupStatus title=$title progress=$progressPercent")
        (activity as? GameActivity)?.recordStartupStatus(detail)
        activity.runOnUiThread {
            if (
                activity.isFinishing ||
                    (Build.VERSION.SDK_INT >= Build.VERSION_CODES.JELLY_BEAN_MR1 &&
                        activity.isDestroyed)
            ) {
                return@runOnUiThread
            }
            val gameActivity = activity as? GameActivity
            if (gameActivity == null) {
                android.util.Log.e(
                    STATUS_TAG,
                    "setStartupStatus ignored: activity is ${activity.javaClass.name}",
                )
            } else {
                gameActivity.updateStartupStatus(detail, progressPercent)
            }
        }
        return 0
    }

    @JvmStatic
    fun hideStartupStatus(): Int {
        val activity = currentActivity()
        if (activity == null) {
            android.util.Log.e(STATUS_TAG, "hideStartupStatus ignored: no activity")
            return -1
        }
        android.util.Log.i(STATUS_TAG, "hideStartupStatus")
        activity.runOnUiThread {
            if (
                activity.isFinishing ||
                    (Build.VERSION.SDK_INT >= Build.VERSION_CODES.JELLY_BEAN_MR1 &&
                        activity.isDestroyed)
            ) {
                return@runOnUiThread
            }
            (activity as? GameActivity)?.hideStartupStatus()
        }
        return 0
    }

    @JvmStatic
    fun getClipboardText(): String? {
        val activity = currentActivity() ?: return null
        val manager =
            activity.getSystemService(Context.CLIPBOARD_SERVICE)
                as? android.content.ClipboardManager ?: return null
        val clip = manager.primaryClip ?: return null
        return clip.getItemAt(0)?.coerceToText(activity)?.toString()
    }

    @JvmStatic
    fun setClipboardText(text: String): Int {
        val activity = currentActivity() ?: return -1
        val manager =
            activity.getSystemService(Context.CLIPBOARD_SERVICE)
                as? android.content.ClipboardManager ?: return -1
        activity.runOnUiThread { manager.setPrimaryClip(ClipData.newPlainText(null, text)) }
        return 0
    }

    @JvmStatic
    fun openUrl(url: String): Int {
        val activity = currentActivity() ?: return -1
        return try {
            activity.runOnUiThread {
                activity.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url)))
            }
            0
        } catch (_: Exception) {
            -1
        }
    }

    @JvmStatic
    fun setOrientation(landscape: Int): Int {
        val activity = currentActivity() ?: return -1
        activity.runOnUiThread {
            activity.requestedOrientation =
                if (landscape != 0) {
                    ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE
                } else {
                    ActivityInfo.SCREEN_ORIENTATION_SENSOR_PORTRAIT
                }
        }
        return 0
    }

    @JvmStatic
    fun getDisplaySize(): Long {
        val activity = currentActivity() ?: return 0L
        val (width, height) =
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                val bounds = activity.windowManager.currentWindowMetrics.bounds
                bounds.width() to bounds.height()
            } else {
                val metrics = android.util.DisplayMetrics()
                @Suppress("DEPRECATION")
                activity.windowManager.defaultDisplay.getRealMetrics(metrics)
                metrics.widthPixels to metrics.heightPixels
            }
        return (width.toLong() shl 32) or (height.toLong() and 0xffffffffL)
    }

    @JvmStatic
    fun setFullscreen(fullscreen: Int): Int {
        val activity = currentActivity() ?: return -1
        activity.runOnUiThread {
            val gameActivity = activity as? GameActivity ?: return@runOnUiThread
            // Desktop windowed preferences must not expose Android system bars over the game.
            gameActivity.enterImmersiveMode()
        }
        return 0
    }

    @JvmStatic fun isImeRequestedVisible(): Boolean = imeRequestedVisible

    @JvmStatic
    fun setImeVisible(visible: Int): Int {
        if (currentActivity() == null) {
            android.util.Log.e(
                "tMLPocketIme",
                "setImeVisible($visible) ignored: no current activity",
            )
            return -1
        }
        imeRequestedVisible = visible != 0
        android.util.Log.i("tMLPocketIme", "setImeVisible($visible)")
        val result =
            if (visible != 0) {
                if (SDLActivity.showTextInput(android.text.InputType.TYPE_CLASS_TEXT, 0, 0, 1, 1)) 0 else -1
            } else {
                val activity = currentActivity() ?: return -1
                // sendCommand queues the hide on the same SDL handler as showTextInput.
                (activity as? GameActivity)?.hideGameKeyboard() ?: return -1
                0
            }
        android.util.Log.i("tMLPocketIme", "setImeVisible($visible) queued result=$result")
        return result
    }

    @JvmStatic fun postImeCommit(text: String) = post(kind = EVENT_IME_COMMIT, text = text)

    @JvmStatic
    fun postImeComposition(text: String) = post(kind = EVENT_IME_COMPOSITION, text = text)

    @JvmStatic
    fun postImeVisibility(visible: Boolean, userDismissal: Boolean = false) {
        if (lastImeVisible == visible && !(userDismissal && imeRequestedVisible)) return
        val wasVisible = lastImeVisible == true
        lastImeVisible = visible
        val dismissed = !visible && (userDismissal || (wasVisible && imeRequestedVisible))
        if (dismissed) imeRequestedVisible = false
        post(kind = EVENT_IME_VISIBILITY, action = if (dismissed) 1 else 0, focused = visible)
    }

    @JvmStatic
    fun hasPermission(permission: String): Boolean {
        val activity = currentActivity() ?: return false
        return Build.VERSION.SDK_INT < Build.VERSION_CODES.M ||
            activity.checkSelfPermission(permission) == PackageManager.PERMISSION_GRANTED
    }

    @JvmStatic
    fun requestPermission(permission: String, requestCode: Int): Int {
        val activity = currentActivity() ?: return -1
        activity.runOnUiThread {
            if (
                Build.VERSION.SDK_INT < Build.VERSION_CODES.M ||
                    activity.checkSelfPermission(permission) == PackageManager.PERMISSION_GRANTED
            ) {
                postPermission(requestCode, true)
            } else {
                activity.requestPermissions(arrayOf(permission), requestCode)
            }
        }
        return 0
    }

    @JvmStatic
    fun showTextPrompt(title: String, password: Int, requestCode: Int): Int {
        val activity = currentActivity() as? GameActivity ?: return -1
        activity.runOnUiThread {
            activity.showTextPrompt(title, password != 0) { text -> postTextResult(requestCode, text) }
        }
        return 0
    }

    @JvmStatic fun postLifecycle(paused: Boolean) = post(kind = EVENT_LIFECYCLE, paused = paused)

    @JvmStatic fun postFocus(focused: Boolean) = post(kind = EVENT_FOCUS, focused = focused)

    @JvmStatic fun postBack() = post(EVENT_BACK)

    fun postKeyboardRequest() = post(EVENT_KEYBOARD_REQUEST)

    @JvmStatic
    fun postText(text: String) {
        if (text.isNotEmpty()) post(EVENT_TEXT, text = text)
    }

    @JvmStatic
    fun postKey(event: KeyEvent) =
        post(
            kind = EVENT_KEY,
            keyCode = event.keyCode,
            pressed = event.action == KeyEvent.ACTION_DOWN,
            action = event.action,
        )

    @JvmStatic
    fun postPermission(requestCode: Int, granted: Boolean) =
        post(kind = EVENT_PERMISSION, requestCode = requestCode, granted = granted)

    @JvmStatic
    fun postLauncherAction(action: Int) = post(kind = EVENT_LAUNCHER_ACTION, action = action)

    private fun postTextResult(requestCode: Int, text: String?) =
        post(kind = EVENT_TEXT, text = text, requestCode = requestCode)

    private fun post(
        kind: Int,
        text: String? = null,
        keyCode: Int = 0,
        pressed: Boolean = false,
        pointerId: Int = 0,
        x: Float = 0f,
        y: Float = 0f,
        action: Int = 0,
        focused: Boolean = false,
        paused: Boolean = false,
        requestCode: Int = 0,
        granted: Boolean = false,
    ) {
        NativeCoreClr.nativePostAndroidEvent(
            kind,
            SystemClock.elapsedRealtimeNanos(),
            text,
            keyCode,
            pressed,
            pointerId,
            x,
            y,
            action,
            focused,
            paused,
            requestCode,
            granted,
        )
    }

    private fun currentActivity(): Activity? = activityReference?.get()
}
