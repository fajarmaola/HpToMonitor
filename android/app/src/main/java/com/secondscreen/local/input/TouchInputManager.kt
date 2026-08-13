package com.secondscreen.local.input

import android.view.GestureDetector
import android.view.MotionEvent
import android.view.View
import com.secondscreen.local.net.ConnectionManager
import com.secondscreen.local.shared.TouchEvent
import kotlin.math.abs

// Translates Android touch gestures into normalized TOUCH messages (PROTOCOL.md §4) and sends
// them to Windows. Coordinates are normalized [0,1] relative to the view so Windows can map
// them onto the virtual Display 2 regardless of resolution differences.
class TouchInputManager(
    private val view: View,
    private val connection: ConnectionManager
) {
    private val gestures = GestureDetector(view.context, object : GestureDetector.SimpleOnGestureListener() {
        override fun onLongPress(e: MotionEvent) {
            connection.sendTouch(0, nx(e.x), ny(e.y), TouchEvent.LONG_PRESS)
        }
        override fun onScroll(e1: MotionEvent?, e2: MotionEvent, dX: Float, dY: Float): Boolean {
            // Two-finger scroll => mouse wheel. Single-finger scroll => drag (handled in onTouch).
            if (e2.pointerCount >= 2) {
                connection.sendTouch(0, nx(e2.x), ny(e2.y), TouchEvent.SCROLL,
                    dx = dX / view.width, dy = dY / view.height)
                return true
            }
            return false
        }
    })

    private var downX = 0f
    private var downY = 0f
    private var dragging = false

    fun attach() {
        view.setOnTouchListener { v, e ->
            if (gestures.onTouchEvent(e)) return@setOnTouchListener true
            when (e.actionMasked) {
                MotionEvent.ACTION_DOWN -> {
                    downX = e.x; downY = e.y; dragging = false
                    connection.sendTouch(0, nx(e.x), ny(e.y), TouchEvent.DOWN)
                }
                MotionEvent.ACTION_MOVE -> {
                    if (e.pointerCount == 1) {
                        if (!dragging && (abs(e.x - downX) > 8 || abs(e.y - downY) > 8)) dragging = true
                        connection.sendTouch(0, nx(e.x), ny(e.y), TouchEvent.MOVE)
                    }
                }
                MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> {
                    connection.sendTouch(0, nx(e.x), ny(e.y), TouchEvent.UP)
                }
            }
            true
        }
    }

    private fun nx(x: Float) = (x / view.width).coerceIn(0f, 1f)
    private fun ny(y: Float) = (y / view.height).coerceIn(0f, 1f)
}
