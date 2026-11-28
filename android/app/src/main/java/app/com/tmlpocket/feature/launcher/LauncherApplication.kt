package app.com.tmlpocket.feature.launcher

import android.app.Application
import android.os.Process

class LauncherApplication : Application() {
    override fun onCreate() {
        super.onCreate()
        if (getProcessName() != packageName) return
        val previous = Thread.getDefaultUncaughtExceptionHandler()
        Thread.setDefaultUncaughtExceptionHandler { thread, error ->
            try {
                runCatching { LauncherDiagnostics.record(this, thread, error) }
            } finally {
                if (previous != null) previous.uncaughtException(thread, error)
                else Process.killProcess(Process.myPid())
            }
        }
    }
}
