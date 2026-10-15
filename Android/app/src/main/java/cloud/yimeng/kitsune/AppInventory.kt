package cloud.yimeng.kitsune

import android.content.Context
import android.content.pm.ApplicationInfo
import android.content.pm.PackageManager
import android.graphics.drawable.Drawable
import android.os.Build
import java.text.Collator
import java.util.Locale

data class AppInfo(val label: String, val packageName: String, val icon: Drawable, val isSystem: Boolean)

/** 分流设置的纯逻辑部分，便于单元测试。 */
object AppFilter {
    const val KEY_MODE = "appFilterMode"
    const val KEY_LIST = "appFilterList"
    const val MODE_OFF = "off"
    const val MODE_WHITELIST = "whitelist"
    const val MODE_BLACKLIST = "blacklist"
    val MODES = listOf(MODE_OFF, MODE_WHITELIST, MODE_BLACKLIST)

    fun modeOf(raw: String?): String = if (raw != null && MODES.contains(raw)) raw else MODE_OFF

    fun parsePackageList(raw: String?): Set<String> =
        raw.orEmpty().split(',').map { it.trim() }.filter { it.isNotEmpty() }.toSet()

    fun serializePackageList(packages: Set<String>): String = packages.sorted().joinToString(",")
}

class AppInventory(private val context: Context) {
    /** 一次性构建设备应用列表；只保留声明 INTERNET 权限或有 LAUNCHER 活动的应用。 */
    fun installedApps(): List<AppInfo> {
        val pm = context.packageManager
        val packages = if (Build.VERSION.SDK_INT >= 33)
            pm.getInstalledPackages(PackageManager.PackageInfoFlags.of(PackageManager.GET_PERMISSIONS.toLong()))
        else
            @Suppress("DEPRECATION") pm.getInstalledPackages(PackageManager.GET_PERMISSIONS)
        val collator = Collator.getInstance(Locale.CHINA)
        return packages.mapNotNull { info ->
            val app = info.applicationInfo ?: return@mapNotNull null
            val hasInternet = info.requestedPermissions?.contains(android.Manifest.permission.INTERNET) == true
            if (!hasInternet && pm.getLaunchIntentForPackage(info.packageName) == null) return@mapNotNull null
            AppInfo(
                label = pm.getApplicationLabel(app).toString(),
                packageName = info.packageName,
                icon = pm.getApplicationIcon(app),
                isSystem = app.flags and ApplicationInfo.FLAG_SYSTEM != 0
            )
        }.sortedWith(compareBy<AppInfo> { it.isSystem }.thenComparator { a, b -> collator.compare(a.label, b.label) })
    }
}
