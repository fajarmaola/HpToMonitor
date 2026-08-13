package com.secondscreen.local

import com.secondscreen.local.net.ConnectionManager
import com.secondscreen.local.video.VideoPipeline

// Process-wide holder so MainActivity (pairing) and MonitorActivity (rendering) share one
// live session. Kept intentionally small.
object Session {
    @Volatile var connection: ConnectionManager? = null
    @Volatile var pipeline: VideoPipeline? = null

    fun teardown() {
        pipeline?.stop(); pipeline = null
        connection?.shutdown(); connection = null
    }
}
