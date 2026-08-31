package com.app.soulexemobile

import android.content.Intent
import android.os.Build
import com.facebook.react.bridge.Promise
import com.facebook.react.bridge.ReactApplicationContext
import com.facebook.react.bridge.ReactContextBaseJavaModule
import com.facebook.react.bridge.ReactMethod

class SoulExeForegroundServiceModule(
  private val context: ReactApplicationContext,
) : ReactContextBaseJavaModule(context) {
  override fun getName(): String = "SoulExeForegroundService"

  @ReactMethod
  fun start(baseUrl: String, session: String, promise: Promise) {
    try {
      val intent = SoulExeForegroundService.startIntent(context, baseUrl, session)
      if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O)
        context.startForegroundService(intent)
      else
        context.startService(intent)
      promise.resolve(null)
    } catch (error: Exception) {
      promise.reject("FOREGROUND_SERVICE_START_FAILED", error)
    }
  }

  @ReactMethod
  fun stop(promise: Promise) {
    try {
      context.startService(Intent(context, SoulExeForegroundService::class.java).apply {
        action = SoulExeForegroundService.ACTION_STOP
      })
      promise.resolve(null)
    } catch (error: Exception) {
      promise.reject("FOREGROUND_SERVICE_STOP_FAILED", error)
    }
  }
}
