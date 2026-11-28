package app.com.tmlpocket.core.platform

object NativeCoreClr {
    init {
        System.loadLibrary("dotnethost")
    }

    @JvmStatic
    external fun nativeLaunch(
        assemblyPath: String,
        args: Array<String>,
        dotnetRoot: String
    ): Int

    @JvmStatic
    external fun nativeLastError(): String

    @JvmStatic
    external fun nativeChdir(path: String): Int

    @JvmStatic
    external fun nativeSetEnv(name: String, value: String): Int

    @JvmStatic
    external fun nativeStartGameLog(path: String): Boolean

    @JvmStatic
    external fun nativeRegisterBridgeActivity(activity: android.app.Activity)

    @JvmStatic
    external fun nativeClearBridgeActivity()

    @JvmStatic
    external fun nativePostAndroidEvent(
        kind: Int,
        timestampNanos: Long,
        text: String?,
        keyCode: Int,
        pressed: Boolean,
        pointerId: Int,
        x: Float,
        y: Float,
        action: Int,
        focused: Boolean,
        paused: Boolean,
        requestCode: Int,
        granted: Boolean
    )
}
