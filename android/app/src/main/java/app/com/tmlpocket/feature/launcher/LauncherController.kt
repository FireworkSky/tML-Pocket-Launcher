package app.com.tmlpocket.feature.launcher

import android.content.Context
import android.content.pm.PackageInfo
import android.content.res.Resources
import android.telephony.TelephonyManager
import android.net.Uri
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import android.util.Log
import app.com.tmlpocket.core.runtime.AssetBootstrap
import app.com.tmlpocket.feature.game.GameCrashReports
import app.com.tmlpocket.feature.files.FileBrowserAccess
import app.com.tmlpocket.feature.installation.GamePackageInstaller
import app.com.tmlpocket.feature.installation.GameInstances
import app.com.tmlpocket.feature.installation.GameStorage
import app.com.tmlpocket.feature.installation.SaveBackupRepository
import app.com.tmlpocket.feature.installation.MobileTerrariaPackage
import app.com.tmlpocket.feature.installation.MobileTerrariaConverter
import app.com.tmlpocket.feature.installation.MobileControlArtPreview
import app.com.tmlpocket.feature.installation.PackageKind
import app.com.tmlpocket.feature.installation.TerrariaSource
import app.com.tmlpocket.feature.mods.ModRepository
import app.com.tmlpocket.feature.mods.ModRepository.Companion.strings
import app.com.tmlpocket.feature.mods.WorkshopContentInstaller
import app.com.tmlpocket.feature.patches.PatchRepository
import app.com.tmlpocket.feature.releases.TmlRelease
import app.com.tmlpocket.feature.releases.TmlReleasePage
import app.com.tmlpocket.feature.releases.TmlReleaseSource
import app.com.tmlpocket.feature.releases.TmlReleaseClient
import app.com.tmlpocket.feature.renderers.AndroidRendererPluginPackageSource
import app.com.tmlpocket.feature.renderers.RendererPluginAssetStager
import app.com.tmlpocket.feature.renderers.RendererPluginOption
import app.com.tmlpocket.feature.renderers.RendererPluginRepository
import app.com.tmlpocket.feature.renderers.RendererSelection
import app.com.tmlpocket.feature.stores.SteamGameDetailsClient
import app.com.tmlpocket.feature.stores.StoreRepository
import java.io.File
import java.io.InterruptedIOException
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean
import org.json.JSONArray
import org.json.JSONObject

/** Serializes mutations and retains tasks without retaining an Activity/WebView. */
internal class LauncherController(private val context: Context) {
    private val main = Handler(Looper.getMainLooper())
    private val worker = Executors.newSingleThreadExecutor()
    private val gameStorage = GameStorage(context)
    private val installer get() = GamePackageInstaller(gameStorage.packageRoot)
    private val mods get() = ModRepository(gameStorage.userData())
    private val mobileControls get() = MobileControlsSettings(gameStorage.userData())
    private val controlPacks = ControlLayoutPacks(File(context.filesDir, "control-layout-packs"))
    private val instances get() = gameStorage.instances()
    private val backups get() = SaveBackupRepository(gameStorage.userData(), File(gameStorage.root(), "backups/${instances.activeId()}"))
    private fun currentInstallation() = installer.installation(instances.versionId())
    private var migratedTouchMod: File? = null
    private val patches = PatchRepository(File(context.filesDir, "patches"))
    private val client = TmlReleaseClient(TmlReleaseSource.forCountry(
        runCatching { context.getSystemService(TelephonyManager::class.java)?.networkCountryIso }.getOrNull(),
        Resources.getSystem().configuration.locales[0].country,
    ))
    private val releases = linkedMapOf<String, TmlRelease>()
    private val stores = StoreRepository(context)
    private val steamDetails = SteamGameDetailsClient(File(context.cacheDir, "steam-details"))
    private val detailsWorker = Executors.newSingleThreadExecutor()
    private val filesWorker = Executors.newSingleThreadExecutor()
    private val fileBrowser = FileBrowserAccess(context)
    val settings = LauncherSettings(context)
    private val rendererRepository = RendererPluginRepository(
        AndroidRendererPluginPackageSource(context),
        RendererPluginAssetStager(File(context.filesDir, "renderers")),
        android.os.Build.VERSION.SDK_INT,
        android.os.Build.SUPPORTED_ABIS.toList(),
    )
    private var cancellation = AtomicBoolean(false)
    @Volatile private var closed = false
    var observer: ((JSONObject) -> Unit)? = null
    private var operationBusy = false
    private val launchWorker = Executors.newSingleThreadExecutor()
    private val downloadGate = Any()
    @Volatile private var transferring = false
    @Volatile private var concurrentLaunch: AtomicBoolean? = null
    @Volatile private var handedOff = false
    private var launchMessage = ""
    val busy get() = operationBusy || concurrentLaunch != null || handedOff

    private var data =
        JSONObject().put("mods", JSONArray()).put("packs", JSONArray()).put("patches", JSONArray())
    private var message = "正在检查安装状态…"
    private var percent = -1
    private var error: String? = null
    private var retry: (() -> Unit)? = null
    private var method = "state"
    private var downloadTaskId = 0L
    @Volatile private var downloadCanPause = false
    private var launchStage: String? = null
    private var lastProgressNanos = 0L
    private var storeState = JSONObject()
    @Volatile private var rendererOptions = emptyList<RendererPluginOption>()

    fun snapshot(): JSONObject =
        JSONObject(data.toString())
            .put("releaseSource", client.source.label)
            .put("systemLocale", context.resources.configuration.locales[0].toLanguageTag())
            .put("busy", busy)
            .put("launchBlocked", busy && (!transferring || concurrentLaunch != null || handedOff))
            .put("launchMessage", launchMessage)
            .put("message", message)
            .put("progress", percent)
            .put("error", error ?: JSONObject.NULL)
            .put("retry", retry != null)
            .put("operation", method)
            .put("downloadCanPause", busy && method == "download" && downloadCanPause)
            .put("launchStage", launchStage ?: JSONObject.NULL)
            .put("storageGranted", gameStorage.granted)
            .put("preferences", settings.read())
            .put("renderers", rendererOptionsJson())
            .put("protocol", 1)

    fun gameHandedOff() {
        handedOff = true
    }

    fun launcherResumed() {
        handedOff = false
        emit()
    }

    fun resumeStores() {
        if (!closed) worker.execute { if (!closed) runCatching { stores.onResume() } }
    }

    fun refreshRendererCatalog() {
        if (!closed) worker.execute {
            val result = runCatching { rendererRepository.discover() }
            result.onSuccess { catalog ->
                rendererOptions = catalog.options
                if (catalog.diagnostics.isNotEmpty()) {
                    android.util.Log.w("tMLPocketRenderers", catalog.diagnostics.joinToString("; "))
                }
            }.onFailure { android.util.Log.e("tMLPocketRenderers", "Renderer discovery failed", it) }
            main.post { if (!closed) emit() }
        }
    }

    fun request(name: String, params: JSONObject, reply: (Result<Any>) -> Unit) {
        if (name in setOf("files.roots", "files.list", "files.image", "files.export", "mods.icons", "import", "controls.read", "controls.packs.list")) {
            if (!closed)
                filesWorker.execute {
                    if (name == "import") {
                        val result = runCatching {
                            fileBrowser
                                .repository()
                                .selection(params.getString("kind"), params.getJSONArray("files"))
                        }
                        main.post {
                            if (!closed)
                                result.fold(
                                    { files ->
                                        importUris(
                                            params.getString("kind"),
                                            files.map { Uri.fromFile(it) },
                                            reply,
                                        )
                                    },
                                    { reply(Result.failure(it)) },
                                )
                        }
                    } else {
                        val result =
                            runCatching<Any> {
                                when (name) {
                                    "files.roots" -> fileBrowser.status()
                                    "files.list" -> fileBrowser.repository().list(params)
                                    "files.image" -> fileBrowser.image(params)
                                    "mods.icons" -> mods.icons(params.getJSONArray("ids").strings())
                                    "controls.read" -> mobileControls.read()
                                    "controls.packs.list" -> controlPacks.list()
                                    else ->
                                        fileBrowser
                                            .repository()
                                            .export(
                                                params.getString("root"),
                                                params.optString("path"),
                                                params.getString("name"),
                                                params.getString("text"),
                                            )
                                }
                            }
                        main.post { if (!closed) reply(result) }
                    }
                }
            return
        }
        when (name) {
            "logs",
            "storage",
            "terraria.mobile.inspect",
            "controls.art",
            "store.steam.details" -> {
                if (!closed)
                    detailsWorker.execute {
                        val result = runCatching {
                            when (name) {
                                "logs" -> JSONObject().put("text", GameCrashReports.readLogs(context))
                                "storage" -> storage()
                                // Texture conversion can run while filesWorker reads the editable layout.
                                "controls.art" -> {
                                    val gameRoot = currentInstallation()?.terrariaRoot
                                    val art = gameRoot?.let {
                                        runCatching { MobileTerrariaPackage.controlArt(context, it) }.getOrNull()
                                    }
                                    MobileControlArtPreview.read(context, art, gameRoot?.resolve("Content"))
                                }
                                "terraria.mobile.inspect" -> MobileTerrariaPackage.inspect(context)
                                    .put("targetTml", MobileTerrariaPackage.TARGET_TML)
                                else -> steamDetails.details(params.optBoolean("refresh"))
                            }
                        }
                        main.post { if (!closed) reply(result) }
                    }
                return
            }
            "store.status" -> {
                if (busy) {
                    reply(Result.success(JSONObject(storeState.toString())))
                    return
                }
                worker.execute {
                    val result = runCatching { stores.request("status", JSONObject()) }
                    main.post {
                        if (!closed) {
                            (result.getOrNull() as? JSONObject)?.let { storeState = it }
                            reply(result)
                        }
                    }
                }
                return
            }
            "cancel" -> {
                if (params.optString("operation") == "launch.check") {
                    concurrentLaunch?.set(true)
                    if (operationBusy && method == "launch.check") cancellation.set(true)
                    reply(Result.success(true))
                    return
                }
                if (params.has("taskId") && (!busy || method != params.optString("operation") ||
                        downloadTaskId != params.optLong("taskId")
                    )
                ) {
                    reply(Result.failure(IllegalStateException("下载任务已结束")))
                    return
                }
                if (busy && method == "download" && !downloadCanPause) {
                    reply(Result.failure(IllegalStateException("发行包正在安装，请等待完成")))
                    return
                }
                cancellation.set(true)
                message = "正在取消…"
                emit()
                reply(Result.success(true))
                return
            }
            "settings.save" -> {
                reply(runCatching {
                    val patch = params.getJSONObject("preferences")
                    check(!busy || LauncherSettings.INSTANCE_FIELDS.none { patch.has(it) }) {
                        "已有任务正在执行，请等待或取消"
                    }
                    if (patch.has("rendererPlugin")) {
                        val identity = patch.getString("rendererPlugin")
                        require(RendererSelection.isAvailable(identity, rendererOptions.map { it.identity }.toSet())) {
                            "所选渲染器不可用，请刷新渲染器列表"
                        }
                    }
                    settings.save(patch)
                })
                emit()
                return
            }
            "retry" -> {
                val action = retry
                if (action == null) reply(Result.failure(IllegalStateException("没有可重试的任务")))
                else {
                    action()
                    reply(Result.success(true))
                }
                return
            }
        }
        if (busy && name == "launch.check" && !gameRunning()) {
            val token = AtomicBoolean(false)
            val canStart = synchronized(downloadGate) {
                if (!transferring || concurrentLaunch != null || handedOff) false
                else {
                    concurrentLaunch = token
                    true
                }
            }
            if (canStart) {
                launchWorker.execute {
                    val result = runCatching { checkLaunch { token.get() || closed } }
                    main.post {
                        try {
                            if (!closed) reply(if (token.get()) Result.failure(IllegalStateException("启动已取消")) else result)
                        } finally {
                            concurrentLaunch = null
                            launchStage = null
                            if (!closed) emit()
                        }
                    }
                }
                return
            }
        }
        if (busy) {
            if (name == "state") reply(Result.success(snapshot()))
            else reply(Result.failure(IllegalStateException("已有任务正在执行，请等待或取消")))
            return
        }
        if (
            name in
                setOf(
                    "download",
                    "versions.select",
                    "versions.uninstall",
                    "instances.create",
                    "instances.select",
                    "instances.rename",
                    "instances.archive",
                    "backups.create",
                    "backups.list",
                    "backups.restore",
                    "backups.import",
                    "backups.export",
                    "backups.delete",
                    "mods.enable",
                    "mods.delete",
                    "packs.apply",
                    "patches.toggle",
                    "patches.delete",
                    "workshop.query",
                    "workshop.details",
                    "workshop.download",
                    "controls.save",
                    "controls.packs.apply",
                    "storage.enable",
                    "launch.check",
                    "terraria.mobile.convert",
                    "terraria.select",
                    "terraria.uninstall",
                    "store.steam.download",
                    "store.gog.download",
                ) && gameRunning()
        ) {
            reply(Result.failure(IllegalStateException("游戏正在运行，请返回并关闭游戏后修改安装或模组")))
            return
        }
        downloadTaskId = params.optLong("taskId")
        execute(name, { request(name, params, {}) }, reply) { cancelled ->
            val download = name in setOf("download", "workshop.download", "store.steam.download", "store.gog.download")
            if (download) checkDownloadNetwork()
            if (download) {
                transferring = true
                main.post { if (!closed) emit() }
            }
            when (name) {
                "instances.create" -> {
                    val preferences = settings.read()
                    val scoped = JSONObject()
                    for (key in LauncherSettings.INSTANCE_FIELDS) if (preferences.has(key)) scoped.put(key, preferences.get(key))
                    val version = instances.versionId()
                        ?: installer.installedVersions().firstOrNull { it.active && it.available }?.id
                    instances.create(params.getString("name"), version, scoped)
                }
                "instances.select" -> { instances.select(params.getString("id")); true }
                "instances.rename" -> { instances.rename(params.getString("id"), params.getString("name")); true }
                "instances.archive" -> { instances.archive(params.getString("id"), params.getBoolean("archived")); true }
                "backups.list" -> backups.list()
                "backups.create" -> backups.create(cancelled)
                "backups.delete" -> { backups.delete(params.getString("id")); true }
                "backups.restore" -> backups.restore(params.getString("id"), cancelled)
                "backups.import" -> {
                    val file = fileBrowser.repository().resolve(params.getString("root"), params.getString("path"))
                    backups.importArchive(file, cancelled)
                }
                "backups.export" -> {
                    val destination = fileBrowser.repository().resolve(params.getString("root"), params.getString("path"), true)
                    val filename = params.getString("name")
                    require(filename.length in 1..128 && filename.endsWith(".zip", true) &&
                        filename.none { it.code < 32 || it in "\\/:*?\"<>|" } && !filename.startsWith('.') && !filename.endsWith(' ')) { "请输入有效的 ZIP 文件名" }
                    val target = File(destination, filename)
                    val source = backups.archive(params.getString("id"))
                    java.nio.file.Files.newOutputStream(target.toPath(), java.nio.file.StandardOpenOption.CREATE_NEW).use { output ->
                        try {
                            source.inputStream().use { input ->
                                val buffer = ByteArray(128 * 1024)
                                while (true) {
                                    check(!cancelled()) { "导出已取消" }
                                    val count = input.read(buffer)
                                    if (count < 0) break
                                    output.write(buffer, 0, count)
                                }
                            }
                        } catch (failure: Throwable) { target.delete(); throw failure }
                    }
                    true
                }
                "controls.save" -> mobileControls.save(params)
                "controls.packs.save" -> controlPacks.save(params.getString("name"), mobileControls.snapshot())
                "controls.packs.apply" -> mobileControls.applyPack(ControlLayoutArchive.read(controlPacks.archive(params.getString("id"))).profile)
                "controls.packs.delete" -> controlPacks.delete(params.getString("id"))
                "store.status",
                "store.steam.login",
                "store.steam.guard",
                "store.steam.reconnect",
                "store.steam.probe",
                "store.steam.verify",
                "store.steam.logout",
                "store.gog.auth",
                "store.gog.login",
                "store.gog.logout",
                "store.gog.installers" ->
                    stores.request(name.removePrefix("store."), params, cancelled, ::progress)
                "store.steam.download",
                "store.gog.download" -> {
                    val downloadParams = if (name == "store.steam.download")
                        JSONObject().put("installedContent", installer.desktopContentForSteamDownload()?.absolutePath)
                    else params
                    val archive =
                        stores.downloadedArchive(
                            stores.request(
                                name.removePrefix("store."),
                                downloadParams,
                                cancelled,
                                ::progress,
                            )
                        )
                    try {
                        awaitDownloadInstall(cancelled)
                        installer.importPackage(
                            PackageKind.TERRARIA,
                            archive,
                            cancelled = cancelled,
                            progress = ::progress,
                            source =
                                if (name == "store.steam.download") TerrariaSource.STEAM
                                else TerrariaSource.GOG,
                            verifiedSteamDownload = name == "store.steam.download",
                        )
                    } finally {
                        archive.delete()
                    }
                    true
                }
                "state", "refresh" -> null
                "versions.select" -> {
                    val id = params.getString("id")
                    require(installer.installedVersions().any { it.id == id && it.available }) { "所选版本不可用" }
                    if (instances.activeId() == GameInstances.DEFAULT) installer.selectVersion(id)
                    else instances.setVersion(id)
                    true
                }
                "versions.uninstall" -> {
                    val selected = instances.versionId()
                    installer.uninstallVersion(params.getString("id"), cancelled, ::progress)
                    if (selected == params.getString("id")) {
                        instances.setVersion(installer.installedVersions().firstOrNull { it.active && it.available }?.id
                            ?: installer.installedVersions().firstOrNull { it.available }?.id)
                    }
                    true
                }
                "releases" -> {
                    var catalogClient = when (params.optString("source", "auto")) {
                        "auto" -> client
                        "gitee" -> TmlReleaseClient(TmlReleaseSource.GITEE)
                        "github" -> TmlReleaseClient(TmlReleaseSource.GITHUB)
                        else -> error("无效的 tModLoader 下载源")
                    }
                    val number = params.optInt("page", 1)
                    var pinnedOnly = false
                    val page = try {
                        catalogClient.releases(number, cancelled)
                    } catch (first: java.io.IOException) {
                        if (number != 1 || cancelled() || Thread.currentThread().isInterrupted) throw first
                        val alternate = TmlReleaseClient(TmlReleaseSource.entries.first { it != catalogClient.source })
                        try {
                            alternate.releases(1, cancelled).also { catalogClient = alternate }
                        } catch (second: java.io.IOException) {
                            if (cancelled() || Thread.currentThread().isInterrupted) throw second
                            pinnedOnly = true
                            TmlReleasePage(listOf(catalogClient.source.defaultRelease()), false)
                        }
                    }
                    page.releases.forEach { releases[catalogClient.source.key(it.id)] = it }
                    JSONObject()
                        .put("source", catalogClient.source.name.lowercase())
                        .put("pinnedOnly", pinnedOnly)
                        .put("hasMore", page.hasMore)
                        .put(
                            "releases",
                            JSONArray(
                                page.releases.map {
                                    JSONObject()
                                        .put("id", catalogClient.source.key(it.id))
                                        .put("tag", it.tag)
                                        .put("name", it.name)
                                        .put("preview", it.preview)
                                        .put("size", it.size)
                                }
                            ),
                        )
                }
                "download" -> {
                    val key = params.getString("id")
                    val source = if (key.startsWith("gitee-")) TmlReleaseSource.GITEE else TmlReleaseSource.GITHUB
                    val id = key.removePrefix("gitee-").toLong()
                    require(id > 0 && source.key(id) == key) { "无效的发行版标识" }
                    val downloadClient = TmlReleaseClient(source)
                    val downloadRoot = File(context.filesDir, "downloads")
                    val record = File(downloadRoot, "release-$key.json")
                    val release = (releases[key] ?: if (!record.isFile) downloadClient.release(id, cancelled) else null)?.also {
                        ModRepository.write(record, JSONObject()
                            .put("tag", it.tag).put("name", it.name).put("preview", it.preview)
                            .put("url", it.downloadUrl).put("size", it.size)
                            .put("digest", it.digest ?: JSONObject.NULL).toString())
                    } ?: run {
                        check(record.isFile && record.length() in 1..65536) { "请重新读取官方版本列表" }
                        val saved = JSONObject(record.readText())
                        TmlRelease(id, saved.getString("tag"), saved.getString("name"),
                            saved.getBoolean("preview"), saved.getString("url"), saved.getLong("size"),
                            if (saved.isNull("digest")) null else saved.getString("digest"))
                    }
                    val archive = downloadRelease(downloadClient, release, cancelled)
                    try {
                        downloadCanPause = false
                        awaitDownloadInstall(cancelled)
                        progress("正在安装 tModLoader 发行包…", -1)
                        installer.importPackage(
                            PackageKind.TMODLOADER,
                            archive,
                            release.tag,
                            { Thread.currentThread().isInterrupted },
                            ::progress,
                        )
                        record.delete()
                    } finally {
                        archive.delete()
                    }
                    true
                }
                "mods.enable" -> {
                    mods.setEnabled(
                        params.getJSONArray("ids").strings().toSet(),
                        params.getBoolean("enabled"),
                    )
                    true
                }
                "mods.delete" -> {
                    mods.delete(params.getString("id"))
                    true
                }
                "packs.save" -> mods.savePack(params.getString("name"))
                "packs.apply" -> {
                    mods.applyPack(params.getString("name"))
                    true
                }
                "packs.delete" -> {
                    mods.deletePack(params.getString("name"))
                    true
                }
                "packs.export" -> mods.packs()
                "patches.toggle" -> {
                    patches.toggle(
                        params.getString("id"),
                        params.getBoolean("enabled"),
                        installer.packageStatus(PackageKind.TMODLOADER),
                    )
                    true
                }
                "patches.delete" -> {
                    patches.delete(params.getString("id"))
                    true
                }
                "workshop.query",
                "workshop.details" -> stores.request(name, params, cancelled, ::progress)
                "workshop.download" -> {
                    val result = stores.request(name, params, cancelled, ::progress) as JSONObject
                    val root = File(context.cacheDir, "store-downloads").canonicalFile
                    val directory = File(result.getString("directory")).canonicalFile
                    val file = File(result.getString("file")).canonicalFile
                    require(directory.parentFile == root && file.parentFile == directory && file.isFile) {
                        "工坊暂存下载路径无效"
                    }
                    try {
                        require(result.getInt("appId") == params.getInt("appId") && result.getString("id") == params.getString("id")) {
                            "下载的工坊项目与请求不一致"
                        }
                        if (cancelled()) throw InterruptedIOException("已取消")
                        awaitDownloadInstall(cancelled)
                        progress("正在安装工坊内容…", 95)
                        val modId = WorkshopContentInstaller(gameStorage.userData(), mods)
                            .install(
                                result.getInt("appId"),
                                result.getString("id"),
                                params.optString("title", result.optString("fileName")),
                                file,
                                installer.packageStatus(PackageKind.TMODLOADER),
                            )
                        JSONObject().put("modId", modId ?: JSONObject.NULL)
                    } finally {
                        directory.deleteRecursively()
                    }
                }
                "storage.enable" -> {
                    progress("正在准备共享游戏目录…", -1)
                    gameStorage.enableShared(cancelled)
                    gameStorage.preparePackages(cancelled, ::progress)
                    storage()
                }
                "cache.clear" -> {
                    // Only launcher-owned temporary files; never WebView data, saves or mods.
                    context.cacheDir
                        .listFiles()
                        ?.filter {
                            it.isFile &&
                                (it.name.startsWith("package-") ||
                                    it.name.startsWith("tml-") ||
                                    it.name.startsWith("workshop-"))
                        }
                        ?.forEach { check(it.delete()) { "无法清理 ${it.name}" } }
                    releaseCacheFiles().forEach { check(it.delete()) { "无法清理 ${it.name}" } }
                    storage()
                }
                "logs" -> JSONObject().put("text", GameCrashReports.readLogs(context))
                "terraria.select" -> {
                    installer.selectTerraria(TerrariaSource.fromId(params.getString("source")))
                    true
                }
                "terraria.uninstall" -> {
                    installer.uninstallTerraria(TerrariaSource.fromId(params.getString("source")), cancelled, ::progress)
                    true
                }
                "terraria.mobile.convert" -> {
                    val packageName = params.optString("packageName").takeIf { it.isNotBlank() }
                    val info = MobileTerrariaPackage.requireInstalled(context, packageName)
                    prepareMobileTerraria(cancelled, info)
                    true
                }
                "launch.check" -> checkLaunch(cancelled)
                else -> error("未知启动器操作：$name")
            }
        }
    }

    private fun awaitDownloadInstall(cancelled: () -> Boolean) {
        // Close launch admission before touching the live installation. An admitted
        // launch holds its reservation through startActivity, before :game is visible.
        synchronized(downloadGate) { transferring = false }
        main.post { if (!closed) emit() }
        if (concurrentLaunch != null || handedOff || gameRunning()) {
            progress("下载已完成，退出游戏后自动安装", 100)
            while (concurrentLaunch != null || handedOff || gameRunning()) {
                if (cancelled()) throw InterruptedIOException("已取消")
                Thread.sleep(300)
            }
        }
        if (cancelled()) throw InterruptedIOException("已取消")
    }

    private fun checkLaunch(cancelled: () -> Boolean): JSONObject {
        launchProgress("resources", "正在检查游戏资源…")
        backups.recover()
        val installed =
            currentInstallation() ?: error("请检查当前实例选择的 Terraria 资源与 tModLoader 版本")
        if (installed.terrariaSource == TerrariaSource.ANDROID) {
            launchProgress("resources", "正在检查手机版触控资源…")
            try {
                MobileTerrariaPackage.requireLaunchArt(
                    settings.localizedContext(), installed, installer.mobileTerrariaPackageName()
                )
            } catch (_: MobileTerrariaPackage.NotInstalled) {
                return JSONObject().put("missingTerrariaPe", true)
            }
        }
        if (installed.requiresSteamVerification) {
            launchProgress("ownership", "正在通过 Steam 验证 Terraria 所有权…")
            check(stores.request("steam.verify", JSONObject(), cancelled) == true) {
                "Steam 未能确认 Terraria 所有权，请登录拥有游戏的账户后重试"
            }
            installed.legacySteamTransaction?.let(installer::rememberLegacySteamVerification)
        }
        check(!cancelled()) { "启动已取消" }
        if (settings.read().optBoolean("autoBackup", false) && backups.hasSaves()) {
            launchProgress("resources", "正在备份角色和世界…")
            backups.create(cancelled)
        }
        launchProgress("mods", "正在检查模组与补丁…")
        AssetBootstrap.migrateTouchMod(gameStorage.userData())
        mods.validateForLaunch(installed.version)
        val args =
            settings.read().optJSONArray("launchArguments")?.strings().orEmpty() +
                patches.arguments(installed.version)
        PatchRepository.validateArguments(args)
        check(!cancelled()) { "启动已取消" }
        launchProgress("handoff", "正在进入游戏…")
        return JSONObject().put("arguments", JSONArray(args)).put("version", installed.version)
    }

    fun controlPackDocument(uri: Uri, exportId: String?, reply: (Result<Any>) -> Unit) {
        if (!closed) filesWorker.execute {
            val result = runCatching {
                require(uri.scheme == "content" && uri.authority != "${context.packageName}.controlpacks") { "文件地址无效" }
                if (exportId == null) {
                    requireNotNull(context.contentResolver.openInputStream(uri)).use(controlPacks::import)
                } else {
                    controlPacks.archive(exportId).inputStream().use { input ->
                        requireNotNull(context.contentResolver.openOutputStream(uri, "wt")).use { input.copyTo(it) }
                    }
                    JSONObject().put("completed", true)
                }
            }
            main.post { if (!closed) reply(result) }
        }
    }

    fun controlPackShare(id: String, reply: (Result<Uri>) -> Unit) {
        if (!closed) filesWorker.execute {
            val result = runCatching {
                val file = controlPacks.archive(id)
                val name = java.util.zip.ZipFile(file).use(ControlLayoutArchive::manifestName)
                val filename = ControlLayoutArchive.filename(name)
                androidx.core.content.FileProvider.getUriForFile(context, "${context.packageName}.controlpacks", file, filename)
            }
            main.post { if (!closed) reply(result) }
        }
    }

    fun importUris(kind: String, uris: List<Uri>, reply: (Result<Any>) -> Unit) {
        if (busy) {
            reply(Result.failure(IllegalStateException("已有任务正在执行")))
            return
        }
        if (gameRunning()) {
            reply(Result.failure(IllegalStateException("游戏正在运行，请关闭游戏后导入")))
            return
        }
        execute("import", { importUris(kind, uris, {}) }, reply) { cancelled ->
            require(
                uris.size in 1..100 &&
                    kind in
                        setOf(
                            "terraria",
                            "terraria-steam",
                            "terraria-gog",
                            "tml",
                            "mod",
                            "patch",
                            "pack",
                        )
            ) {
                "不支持的导入类型"
            }
            uris.forEachIndexed { index, uri ->
                progress("正在读取文件 ${index + 1} / ${uris.size}…", -1)
                val file = if (kind == "mod") mods.createImportTemp()
                    else File.createTempFile("package-", ".import", context.cacheDir)
                try {
                    val limit =
                        when (kind) {
                            "mod" -> 1024L * 1024 * 1024
                            "patch",
                            "pack" -> 1024L * 1024
                            else -> 8L * 1024 * 1024 * 1024
                        }
                    if (uri.scheme == "file") {
                        val sourceFile = File(requireNotNull(uri.path))
                        val rootSelection = fileBrowser.repository()
                        val valid = rootSelection.roots()
                        check(
                            (0 until valid.length()).any { index ->
                                val root = valid.getJSONObject(index)
                                val base = File(root.getString("path")).canonicalFile
                                val path = sourceFile.canonicalFile.toPath()
                                path.startsWith(base.toPath()) &&
                                    runCatching {
                                            rootSelection.resolve(
                                                root.getString("id"),
                                                base
                                                    .toPath()
                                                    .relativize(path)
                                                    .toString()
                                                    .replace('\\', '/'),
                                            )
                                        }
                                        .isSuccess
                            }
                        ) {
                            "所选文件不可访问，请重新选择"
                        }
                    }
                    context.contentResolver.openInputStream(uri)?.use { input ->
                        file.outputStream().use { output ->
                            val buffer = ByteArray(64 * 1024)
                            var copied = 0L
                            while (true) {
                                if (cancelled()) throw InterruptedIOException("已取消")
                                val count = input.read(buffer)
                                if (count < 0) break
                                copied += count
                                require(copied <= limit) { "文件超过导入上限" }
                                output.write(buffer, 0, count)
                            }
                        }
                    } ?: error("无法读取所选文件")
                    when (kind) {
                        "terraria",
                        "terraria-steam",
                        "tml" ->
                            installer.importPackage(
                                if (kind == "tml") PackageKind.TMODLOADER else PackageKind.TERRARIA,
                                file,
                                cancelled = cancelled,
                                progress = ::progress,
                            )
                        "terraria-gog" -> {
                            val archive = stores.importGog(file, cancelled, ::progress)
                            try {
                                installer.importPackage(
                                    PackageKind.TERRARIA,
                                    archive,
                                    cancelled = cancelled,
                                    progress = ::progress,
                                    source = TerrariaSource.GOG,
                                )
                            } finally {
                                archive.delete()
                            }
                        }
                        "mod" -> mods.importFile(file)
                        "patch" -> patches.importFile(file)
                        "pack" -> {
                            val displayName =
                                (if (uri.scheme == "file") File(requireNotNull(uri.path)).name
                                    else
                                        context.contentResolver
                                            .query(
                                                uri,
                                                arrayOf(
                                                    android.provider.OpenableColumns
                                                        .DISPLAY_NAME
                                                ),
                                                null,
                                                null,
                                                null,
                                            )
                                            ?.use {
                                                if (it.moveToFirst()) it.getString(0) else null
                                            })
                                    ?.substringBeforeLast('.')
                                    ?.take(80) ?: "导入的模组包"
                            mods.importPack(file, displayName)
                        }
                    }
                } finally {
                    file.delete()
                }
            }
            true
        }
    }

    fun close() {
        observer = null
        closed = true
        cancellation.set(true)
        launchWorker.shutdownNow()
        worker.shutdownNow()
        detailsWorker.shutdownNow()
        filesWorker.shutdownNow()
    }

    private fun execute(
        name: String,
        again: () -> Unit,
        reply: (Result<Any>) -> Unit,
        action: (cancelled: () -> Boolean) -> Any?,
    ) {
        if (closed) return
        val startedAt = SystemClock.elapsedRealtime()
        Log.i("tMLPocketLauncher", "Operation started: $name")
        operationBusy = true
        method = name
        downloadCanPause = name == "download"
        launchStage = if (name == "launch.check") "resources" else null
        message = "正在处理…"
        percent = -1
        error = null
        retry = null
        lastProgressNanos = 0
        val token = AtomicBoolean(false)
        cancellation = token
        emit()
        val browsingWorkshop = name == "workshop.query" || name == "workshop.details"
        worker.execute {
            var result = runCatching {
                val cancelled = { token.get() || Thread.currentThread().isInterrupted }
                if (!browsingWorkshop && !gameRunning() && !gameStorage.sharedEnabled && gameStorage.granted && name != "storage.enable") {
                    progress("正在准备共享游戏目录…", -1)
                    gameStorage.enableShared(cancelled)
                }
                if (!browsingWorkshop && !gameRunning() && name != "storage.enable") gameStorage.preparePackages(cancelled, ::progress)
                action(cancelled)
            }
            // A failed transfer touched only staging files. Avoid recovery writes
            // from readState while a concurrent launch is preparing the same instance.
            val failedDuringTransfer = synchronized(downloadGate) {
                transferring.also { transferring = false }
            }
            val refreshed = runCatching {
                if (browsingWorkshop || failedDuringTransfer || concurrentLaunch != null || handedOff) null else readState()
            }
            if (refreshed.isFailure && result.isSuccess)
                result = Result.failure(refreshed.exceptionOrNull()!!)
            val elapsed = SystemClock.elapsedRealtime() - startedAt
            val operationFailure = result.exceptionOrNull()
            when {
                token.get() -> Log.i("tMLPocketLauncher", "Operation cancelled: $name (${elapsed}ms)")
                operationFailure != null -> {
                    // Exception messages can contain account responses or credentials.
                    // Keep the type and call sites without logging request/response data.
                    val frames = operationFailure.stackTrace.take(12).joinToString("\n    at ")
                    Log.e("tMLPocketLauncher", "Operation failed: $name (${elapsed}ms), ${operationFailure.javaClass.name}\n    at $frames")
                }
                else -> Log.i("tMLPocketLauncher", "Operation completed: $name (${elapsed}ms)")
            }
            main.post {
                if (closed) return@post
                refreshed.getOrNull()?.let { data = it }
                operationBusy = false
                val failure = result.exceptionOrNull()
                error =
                    when {
                        failure != null ->
                            failure.message?.takeIf { it.isNotBlank() }
                                ?: "操作失败（${failure.javaClass.simpleName}）"
                        token.get() && name != "download" -> "操作已取消"
                        else -> null
                    }
                retry =
                    if (
                        error != null &&
                            name != "launch.check" &&
                            !name.startsWith("store.")
                    )
                        again
                    else null
                message = error ?: "操作完成"
                percent = if (error == null) 100 else -1
                emit()
                reply(
                    if (error == null) Result.success(result.getOrNull() ?: snapshot())
                    else Result.failure(IllegalStateException(error, failure))
                )
            }
        }
    }

    private fun checkDownloadNetwork() {
        if (settings.read().optBoolean("wifiOnly")) {
            val network = context.getSystemService(android.net.ConnectivityManager::class.java)
            require(network != null && !network.isActiveNetworkMetered) { "已启用仅在 Wi-Fi 下下载，请连接不计费网络或关闭此选项" }
        }
    }

    private fun downloadRelease(client: TmlReleaseClient, release: TmlRelease, cancelled: () -> Boolean): File {
        fun fetch(provider: TmlReleaseClient, item: TmlRelease): File {
            progress("正在从 ${provider.source.label} 下载 ${item.tag}…", 0)
            return provider.download(item, File(context.filesDir, "downloads"), cancelled) { done, total ->
                progress("正在下载 ${item.tag} · ${done / 1048576} MB",
                    if (total > 0) (done * 100 / total).toInt() else -1)
            }
        }
        try {
            return fetch(client, release)
        } catch (e: TmlReleaseClient.DownloadException) {
            if (release.tag != TmlReleaseSource.DEFAULT_TAG || cancelled() ||
                e.reason !in setOf(TmlReleaseClient.DownloadError.NETWORK, TmlReleaseClient.DownloadError.HTTP)) throw e
            val other = TmlReleaseClient(TmlReleaseSource.entries.first { it != client.source })
            return fetch(other, other.source.defaultRelease())
        }
    }

    private fun readState(): JSONObject {
        val userData = gameStorage.userData()
        if (!gameRunning()) {
            if (gameStorage.sharedEnabled) instances.ensureModsDirectory(instances.activeId())
            backups.recover()
        }
        if (!gameRunning() && migratedTouchMod != userData) {
            AssetBootstrap.migrateTouchMod(userData)
            migratedTouchMod = userData
        }
        val packages = installer.launcherStatus(instances.versionId())
        val runtime =
            runCatching {
                    context.assets.open("runtime/dotnet.tar.xz").close()
                    context.assets.open("launcher/manifest.json").close()
                    true
                }
                .getOrDefault(false)
        return packages
            .put("ready", packages.getBoolean("ready") && runtime)
            .put("instances", instances.list())
            .put("activeInstance", instances.activeId())
            .put("runtime", runtime)
            .put("mods", mods.list())
            .put("packs", mods.packs())
            .put("patches", patches.list())
            .put("appVersion", app.com.tmlpocket.BuildConfig.VERSION_NAME)
    }

    private fun prepareMobileTerraria(cancelled: () -> Boolean, info: PackageInfo) {
        val target = MobileTerrariaPackage.TARGET_TML
        val fingerprint = MobileTerrariaPackage.fingerprint(info)
        if (!installer.mobileTerrariaIsCurrent(fingerprint, target)) {
            installer.installMobileTerraria(
                info.versionName.orEmpty().ifBlank { info.longVersionCode.toString() },
                fingerprint,
                target,
                info.packageName,
                cancelled,
                ::progress,
            ) { destination ->
                MobileTerrariaConverter.convertInstalled(context, destination, info.packageName) { stage, current, total ->
                    if (cancelled()) return@convertInstalled false
                    val (label, value) = when (stage) {
                        "extract" -> "正在提取 Terraria PE 资源…" to 0
                        "assets" -> "正在转换贴图、字体和音效（$current/$total）…" to (5 + current * 55 / total)
                        "music" -> "正在转换音乐（$current/$total）…" to (60 + current * 29 / total)
                        else -> "正在校验转换资源…" to 90
                    }
                    progress(label, value)
                    true
                }
                check(MobileTerrariaPackage.installed(context, info.packageName)?.let(MobileTerrariaPackage::fingerprint) == fingerprint) {
                    "转换期间 Terraria PE 已更新，请重新转换"
                }
            }
        }
        check(!cancelled()) { "资源转换已取消" }
        installer.selectTerraria(TerrariaSource.ANDROID)
    }

    private fun releaseCacheFiles(): List<File> =
        File(context.filesDir, "downloads").listFiles().orEmpty().filter {
            it.isFile && (it.name.startsWith("tml-") || it.name.startsWith("release-"))
        }

    private fun storage(): JSONObject {
        val root = if (gameStorage.sharedEnabled) gameStorage.sharedRoot else context.filesDir
        val userData = runCatching { gameStorage.userData() }.getOrDefault(File(root, "userdata"))
        return JSONObject()
            .put("games", File(gameStorage.packageRoot, "games").absolutePath)
            .put("userdata", userData.absolutePath)
            .put("mods", File(userData, "Mods").absolutePath)
            .put("cache", context.cacheDir.absolutePath)
            .put("availableBytes", root.usableSpace)
            .put("shared", gameStorage.sharedEnabled)
            .put("storageGranted", gameStorage.granted)
            .put("sharedExists", gameStorage.sharedExists)
            .put("sharedRoot", gameStorage.sharedRoot.absolutePath)
            .put(
                "cacheBytes",
                (context.cacheDir
                    .listFiles()
                    ?.filter {
                        it.isFile &&
                            (it.name.startsWith("package-") ||
                                it.name.startsWith("tml-") ||
                                it.name.startsWith("workshop-"))
                    }
                    ?.sumOf { it.length() } ?: 0L) + releaseCacheFiles().sumOf { it.length() },
            )
    }

    private fun launchProgress(stage: String, text: String) {
        Log.i("tMLPocketLauncher", "Game launch stage: $stage")
        main.post {
            if (!closed) {
                launchStage = stage
                launchMessage = text
                if (method == "launch.check") {
                    message = text
                    percent = -1
                }
                emit()
            }
        }
    }

    private fun progress(text: String, value: Int) {
        val now = System.nanoTime()
        if (value != 100 && now - lastProgressNanos < 100_000_000) return
        lastProgressNanos = now
        val translated =
            when {
                text == "Extracting package" -> "正在解压发行包…"
                text.startsWith("Extracting ") -> "正在解压：${text.removePrefix("Extracting ")}"
                text == "Validating package" -> "正在校验发行包…"
                text == "Installing package" -> "正在安装发行包…"
                text == "Package installed" -> "发行包已安装。"
                else -> text
            }
        main.post {
            if (!closed) {
                message = translated
                percent = value
                emit()
            }
        }
    }

    private fun emit() {
        observer?.invoke(snapshot())
    }

    private fun rendererOptionsJson(): JSONArray {
        val result = JSONArray()
            .put(JSONObject().put("identity", "native").put("packageName", "").put("packageVersion", "").put("rendererId", "native").put("displayName", "系统原生 OpenGL ES 3").put("description", "").put("backend", "native"))
            .put(JSONObject().put("identity", RendererSelection.VULKAN).put("packageName", "").put("packageVersion", "").put("rendererId", RendererSelection.VULKAN).put("displayName", "SDL GPU · Vulkan").put("description", "SDL2-compat + SDL3 GPU").put("backend", "vulkan"))
        rendererOptions.forEach { option ->
            result.put(JSONObject()
                .put("identity", option.identity)
                .put("packageName", option.packageName)
                .put("packageVersion", option.packageVersionName ?: "")
                .put("rendererId", option.rendererId)
                .put("displayName", option.displayName)
                .put("description", option.description)
                .put("backend", option.backend.id))
        }
        return result
    }

    private fun gameRunning(): Boolean =
        (context.getSystemService(Context.ACTIVITY_SERVICE) as android.app.ActivityManager)
            .runningAppProcesses
            ?.any { it.processName == context.packageName + ":game" } == true
}
