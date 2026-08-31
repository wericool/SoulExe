package com.app.soulexemobile

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.graphics.Color
import android.net.Uri
import android.os.Build
import android.os.IBinder
import androidx.core.app.NotificationCompat
import org.json.JSONArray
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL
import java.util.concurrent.Executors
import java.util.concurrent.ScheduledFuture
import java.util.concurrent.TimeUnit
import kotlin.math.absoluteValue

class SoulExeForegroundService : Service() {
  companion object {
    const val ACTION_START = "com.app.soulexemobile.action.START_BACKGROUND_LINK"
    const val ACTION_STOP = "com.app.soulexemobile.action.STOP_BACKGROUND_LINK"

    private const val EXTRA_BASE_URL = "base_url"
    private const val EXTRA_SESSION = "session"
    private const val PREFS = "soulexe_foreground_link"
    private const val KEY_BASE_URL = "base_url"
    private const val KEY_SESSION = "session"
    private const val KEY_APP_VISIBLE = "app_visible"
    private const val KEY_BASELINE_READY = "baseline_ready"
    private const val KEY_LAST_PREFIX = "last_message_"
    private const val SERVICE_CHANNEL = "soulexe-background-link"
    private const val MESSAGE_CHANNEL = "soulexe-messages"
    private const val SERVICE_NOTIFICATION_ID = 14001

    fun startIntent(context: Context, baseUrl: String, session: String) =
      Intent(context, SoulExeForegroundService::class.java).apply {
        action = ACTION_START
        putExtra(EXTRA_BASE_URL, baseUrl.trimEnd('/'))
        putExtra(EXTRA_SESSION, session)
      }

    fun setAppVisible(context: Context, visible: Boolean) {
      context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        .edit()
        .putBoolean(KEY_APP_VISIBLE, visible)
        .apply()
    }
  }

  private val worker = Executors.newSingleThreadScheduledExecutor()
  private var polling: ScheduledFuture<*>? = null
  private var consecutiveFailures = 0

  override fun onCreate() {
    super.onCreate()
    createNotificationChannels()
  }

  override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
    if (intent?.action == ACTION_STOP) {
      clearConnection()
      stopForeground(STOP_FOREGROUND_REMOVE)
      stopSelf()
      return START_NOT_STICKY
    }

    val preferences = getSharedPreferences(PREFS, MODE_PRIVATE)
    val nextBaseUrl = intent?.getStringExtra(EXTRA_BASE_URL)?.trimEnd('/')
    val nextSession = intent?.getStringExtra(EXTRA_SESSION)
    if (!nextBaseUrl.isNullOrBlank() && !nextSession.isNullOrBlank()) {
      val connectionChanged =
        nextBaseUrl != preferences.getString(KEY_BASE_URL, null) ||
          nextSession != preferences.getString(KEY_SESSION, null)
      preferences.edit()
        .putString(KEY_BASE_URL, nextBaseUrl)
        .putString(KEY_SESSION, nextSession)
        .apply()
      if (connectionChanged) resetMessageBaseline(preferences)
    }

    val baseUrl = preferences.getString(KEY_BASE_URL, null)
    val session = preferences.getString(KEY_SESSION, null)
    if (baseUrl.isNullOrBlank() || session.isNullOrBlank()) {
      stopSelf()
      return START_NOT_STICKY
    }

    startForeground(
      SERVICE_NOTIFICATION_ID,
      serviceNotification("Фоновая связь с ПК активна"),
    )
    if (polling == null || polling?.isCancelled == true) {
      polling = worker.scheduleWithFixedDelay(
        { pollSafely() },
        0,
        12,
        TimeUnit.SECONDS,
      )
    }
    return START_STICKY
  }

  override fun onBind(intent: Intent?): IBinder? = null

  override fun onDestroy() {
    polling?.cancel(true)
    polling = null
    worker.shutdownNow()
    super.onDestroy()
  }

  private fun pollSafely() {
    try {
      pollConversations()
      if (consecutiveFailures > 0) {
        consecutiveFailures = 0
        updateServiceNotification("Фоновая связь с ПК активна")
      }
    } catch (_: Exception) {
      consecutiveFailures++
      if (consecutiveFailures == 2)
        updateServiceNotification("Ожидаю подключения к SoulExe на ПК")
    }
  }

  private fun pollConversations() {
    val preferences = getSharedPreferences(PREFS, MODE_PRIVATE)
    val baseUrl = preferences.getString(KEY_BASE_URL, null) ?: return
    val session = preferences.getString(KEY_SESSION, null) ?: return
    val connection = URL("$baseUrl/api/conversations?take=1")
      .openConnection() as HttpURLConnection
    try {
      connection.requestMethod = "GET"
      connection.connectTimeout = 8_000
      connection.readTimeout = 8_000
      connection.useCaches = false
      connection.setRequestProperty("Accept", "application/json")
      connection.setRequestProperty("X-SoulExe-Session", session)
      if (connection.responseCode != HttpURLConnection.HTTP_OK)
        throw IllegalStateException("SoulExe returned ${connection.responseCode}")
      val payload = connection.inputStream.bufferedReader(Charsets.UTF_8).use { it.readText() }
      processConversations(JSONArray(payload), preferences)
    } finally {
      connection.disconnect()
    }
  }

  private fun processConversations(conversations: JSONArray, preferences: android.content.SharedPreferences) {
    val baselineReady = preferences.getBoolean(KEY_BASELINE_READY, false)
    val appVisible = preferences.getBoolean(KEY_APP_VISIBLE, false)
    val editor = preferences.edit()
    for (index in 0 until conversations.length()) {
      val conversation = conversations.optJSONObject(index) ?: continue
      val conversationId = conversation.optString("id")
      if (conversationId.isBlank()) continue
      val messages = conversation.optJSONArray("messages") ?: continue
      if (messages.length() == 0) continue
      val message = messages.optJSONObject(messages.length() - 1) ?: continue
      val messageId = message.optString("id")
      if (messageId.isBlank()) continue
      val key = KEY_LAST_PREFIX + conversationId
      val previousMessageId = preferences.getString(key, null)
      editor.putString(key, messageId)
      if (
        baselineReady &&
        previousMessageId != null &&
        previousMessageId != messageId &&
        !appVisible &&
        isCharacterMessage(conversation, message)
      ) {
        showMessageNotification(conversationId, conversation, message)
      }
    }
    editor.putBoolean(KEY_BASELINE_READY, true).apply()
  }

  private fun isCharacterMessage(conversation: JSONObject, message: JSONObject): Boolean {
    val authorParticipantId = message.optString("authorParticipantId")
    if (authorParticipantId.isBlank()) return false
    val participants = conversation.optJSONArray("participants") ?: return false
    for (index in 0 until participants.length()) {
      val participant = participants.optJSONObject(index) ?: continue
      if (
        participant.optString("id") == authorParticipantId &&
        participant.optString("kind").equals("Character", ignoreCase = true)
      ) return true
    }
    return false
  }

  private fun showMessageNotification(
    conversationId: String,
    conversation: JSONObject,
    message: JSONObject,
  ) {
    val author = message.optString("author").ifBlank {
      conversation.optString("name").ifBlank { "SoulExe" }
    }
    val text = cleanMessage(message.optString("content"))
    val intent = Intent(this, MainActivity::class.java).apply {
      flags = Intent.FLAG_ACTIVITY_SINGLE_TOP or Intent.FLAG_ACTIVITY_CLEAR_TOP
      data = Uri.parse("manussoulexemobile://conversation/$conversationId")
    }
    val pendingIntent = PendingIntent.getActivity(
      this,
      conversationId.hashCode(),
      intent,
      PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
    )
    val notification = NotificationCompat.Builder(this, MESSAGE_CHANNEL)
      .setSmallIcon(R.mipmap.ic_launcher_monochrome)
      .setContentTitle(author)
      .setContentText(text)
      .setStyle(NotificationCompat.BigTextStyle().bigText(text))
      .setColor(Color.rgb(139, 92, 246))
      .setAutoCancel(true)
      .setContentIntent(pendingIntent)
      .setPriority(NotificationCompat.PRIORITY_HIGH)
      .setCategory(NotificationCompat.CATEGORY_MESSAGE)
      .build()
    getSystemService(NotificationManager::class.java).notify(
      20_000 + (conversationId.hashCode().absoluteValue % 10_000),
      notification,
    )
  }

  private fun cleanMessage(value: String): String {
    val cleaned = value
      .replace(Regex("<think\\b[^>]*>[\\s\\S]*?</think>", RegexOption.IGNORE_CASE), " ")
      .replace(Regex("[*_`#>]+"), "")
      .replace(Regex("\\s+"), " ")
      .trim()
    if (cleaned.isBlank()) return "Новое сообщение"
    return if (cleaned.length <= 180) cleaned else cleaned.take(177) + "…"
  }

  private fun serviceNotification(status: String) =
    NotificationCompat.Builder(this, SERVICE_CHANNEL)
      .setSmallIcon(R.mipmap.ic_launcher_monochrome)
      .setContentTitle("SoulExe")
      .setContentText(status)
      .setColor(Color.rgb(139, 92, 246))
      .setOngoing(true)
      .setOnlyAlertOnce(true)
      .setPriority(NotificationCompat.PRIORITY_LOW)
      .setCategory(NotificationCompat.CATEGORY_SERVICE)
      .setContentIntent(
        PendingIntent.getActivity(
          this,
          SERVICE_NOTIFICATION_ID,
          Intent(this, MainActivity::class.java).apply {
            flags = Intent.FLAG_ACTIVITY_SINGLE_TOP or Intent.FLAG_ACTIVITY_CLEAR_TOP
          },
          PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        ),
      )
      .build()

  private fun updateServiceNotification(status: String) {
    getSystemService(NotificationManager::class.java)
      .notify(SERVICE_NOTIFICATION_ID, serviceNotification(status))
  }

  private fun createNotificationChannels() {
    if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) return
    val manager = getSystemService(NotificationManager::class.java)
    manager.createNotificationChannel(
      NotificationChannel(
        SERVICE_CHANNEL,
        "Фоновая связь SoulExe",
        NotificationManager.IMPORTANCE_LOW,
      ).apply {
        description = "Поддерживает связь мобильного SoulExe с приложением на ПК"
        setShowBadge(false)
      },
    )
    manager.createNotificationChannel(
      NotificationChannel(
        MESSAGE_CHANNEL,
        "Сообщения персонажей",
        NotificationManager.IMPORTANCE_HIGH,
      ).apply {
        description = "Новые сообщения из разговоров SoulExe"
        enableVibration(true)
      },
    )
  }

  private fun resetMessageBaseline(preferences: android.content.SharedPreferences) {
    val editor = preferences.edit().putBoolean(KEY_BASELINE_READY, false)
    preferences.all.keys
      .filter { it.startsWith(KEY_LAST_PREFIX) }
      .forEach(editor::remove)
    editor.apply()
  }

  private fun clearConnection() {
    val preferences = getSharedPreferences(PREFS, MODE_PRIVATE)
    val editor = preferences.edit().clear()
    editor.apply()
  }
}
