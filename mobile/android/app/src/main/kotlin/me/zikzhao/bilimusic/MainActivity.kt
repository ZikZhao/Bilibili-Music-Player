package me.zikzhao.bilimusic

import android.app.NotificationManager
import android.content.Context
import android.os.Build
import androidx.annotation.NonNull
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodChannel
import com.ryanheise.audioservice.AudioServiceActivity

class MainActivity: AudioServiceActivity() {
    private val CHANNEL = "com.bilibili.music/utils"

    override fun configureFlutterEngine(@NonNull flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        MethodChannel(flutterEngine.dartExecutor.binaryMessenger, CHANNEL).setMethodCallHandler {
            call, result ->
            if (call.method == "deleteNotificationChannel") {
                val channelId = call.argument<String>("channelId")
                if (channelId != null) {
                    deleteNotificationChannel(channelId)
                    result.success(null)
                } else {
                    result.error("INVALID_ARGUMENT", "Channel ID is null", null)
                }
            } else {
                result.notImplemented()
            }
        }
    }

    private fun deleteNotificationChannel(channelId: String) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val notificationManager = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
            notificationManager.deleteNotificationChannel(channelId)
        }
    }
}
