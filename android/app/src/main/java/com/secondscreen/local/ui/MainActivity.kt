package com.secondscreen.local.ui

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.content.ContextCompat
import com.secondscreen.local.Session
import com.secondscreen.local.net.ClientState
import com.secondscreen.local.net.ConnectionManager
import com.secondscreen.local.net.DiscoveryManager
import com.secondscreen.local.net.HostPeer
import kotlinx.coroutines.launch

class MainActivity : ComponentActivity() {

    private val notifPermission = registerForActivityResult(
        ActivityResultContracts.RequestPermission()) { }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (Build.VERSION.SDK_INT >= 33 &&
            ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS)
            != PackageManager.PERMISSION_GRANTED) {
            notifPermission.launch(Manifest.permission.POST_NOTIFICATIONS)
        }
        setContent { SecondScreenTheme { HomeScreen() } }
    }

    @Composable
    private fun HomeScreen() {
        val scope = rememberCoroutineScope()
        val discovery = remember { DiscoveryManager() }
        var hosts by remember { mutableStateOf<List<HostPeer>>(emptyList()) }
        var searching by remember { mutableStateOf(false) }

        val connection = remember {
            Session.connection ?: ConnectionManager(applicationContext).also { Session.connection = it }
        }
        val state by connection.state.collectAsState()
        val needPin by connection.needPin.collectAsState()
        val error by connection.error.collectAsState()

        var pinInput by remember { mutableStateOf("") }

        // Move to Monitor Mode once streaming begins.
        LaunchedEffect(state) {
            if (state == ClientState.Streaming) {
                startActivity(Intent(this@MainActivity, MonitorActivity::class.java))
            }
        }

        Surface(Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
            Column(Modifier.fillMaxSize().padding(24.dp)) {
                Text("HP KE MONITOR", fontSize = 22.sp, fontWeight = FontWeight.Bold,
                    color = MaterialTheme.colorScheme.onBackground)
                Text("Penerima • layar kedua offline", fontSize = 13.sp,
                    color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.6f))

                Spacer(Modifier.height(20.dp))

                StatusPill(state)

                Spacer(Modifier.height(16.dp))

                Button(
                    onClick = {
                        searching = true
                        scope.launch {
                            hosts = discovery.discover()
                            searching = false
                        }
                    },
                    shape = RoundedCornerShape(12.dp),
                    modifier = Modifier.fillMaxWidth()
                ) { Text(if (searching) "Mencari PC…" else "Cari PC") }

                Spacer(Modifier.height(16.dp))
                Text("PC TERSEDIA", fontSize = 11.sp,
                    color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.6f))
                Spacer(Modifier.height(8.dp))

                LazyColumn(Modifier.weight(1f)) {
                    items(hosts) { host ->
                        HostRow(host) { connection.connect(host) }
                    }
                    if (hosts.isEmpty() && !searching) {
                        item {
                            Text("Belum ada PC ditemukan. Pastikan aplikasi Windows berjalan di Wi-Fi yang sama, lalu tekan Cari.",
                                fontSize = 13.sp,
                                color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.5f),
                                modifier = Modifier.padding(top = 12.dp))
                        }
                    }
                }

                error?.let {
                    Text(it, color = MaterialTheme.colorScheme.error, fontSize = 13.sp,
                        modifier = Modifier.padding(top = 8.dp))
                }

                Text("PT Teleraya Digital Group • company.teleraya.com",
                    fontSize = 11.sp,
                    color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.4f),
                    modifier = Modifier.fillMaxWidth().padding(top = 10.dp),
                    textAlign = androidx.compose.ui.text.style.TextAlign.Center)
            }
        }

        if (needPin) {
            AlertDialog(
                onDismissRequest = { },
                title = { Text("Masukkan kode sambungan") },
                text = {
                    Column {
                        Text("Ketik kode 6 digit yang muncul di aplikasi Windows.")
                        Spacer(Modifier.height(12.dp))
                        OutlinedTextField(
                            value = pinInput,
                            onValueChange = { if (it.length <= 6 && it.all(Char::isDigit)) pinInput = it },
                            keyboardOptions = androidx.compose.foundation.text.KeyboardOptions(
                                keyboardType = KeyboardType.Number),
                            singleLine = true
                        )
                    }
                },
                confirmButton = {
                    TextButton(
                        onClick = { connection.submitPin(pinInput); pinInput = "" },
                        enabled = pinInput.length == 6
                    ) { Text("Sambung") }
                },
                dismissButton = {
                    TextButton(onClick = { connection.disconnect() }) { Text("Batal") }
                }
            )
        }
    }

    @Composable
    private fun StatusPill(state: ClientState) {
        val label = when (state) {
            ClientState.Idle -> "SIAP"
            ClientState.Discovering -> "MENCARI"
            ClientState.Connecting -> "MENYAMBUNG"
            ClientState.Pairing, ClientState.AwaitingPin -> "PAIRING"
            ClientState.Configuring -> "MENYIAPKAN"
            ClientState.Streaming -> "TERSAMBUNG"
            ClientState.Reconnecting -> "MENYAMBUNG ULANG"
            ClientState.Disconnected -> "TERPUTUS"
            ClientState.Error -> "ERROR"
        }
        Surface(color = MaterialTheme.colorScheme.surfaceVariant, shape = RoundedCornerShape(20.dp)) {
            Text(label, fontSize = 12.sp, fontWeight = FontWeight.Bold,
                color = if (state == ClientState.Streaming) MaterialTheme.colorScheme.primary
                        else MaterialTheme.colorScheme.onSurface,
                modifier = Modifier.padding(horizontal = 14.dp, vertical = 6.dp))
        }
    }

    @Composable
    private fun HostRow(host: HostPeer, onConnect: () -> Unit) {
        Surface(
            color = MaterialTheme.colorScheme.surface,
            shape = RoundedCornerShape(12.dp),
            modifier = Modifier.fillMaxWidth().padding(vertical = 6.dp)
        ) {
            Row(Modifier.padding(16.dp), verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) {
                    Text(host.name, fontSize = 16.sp, fontWeight = FontWeight.SemiBold,
                        color = MaterialTheme.colorScheme.onSurface)
                    Text(host.ip, fontSize = 12.sp,
                        color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.6f))
                }
                Button(onClick = onConnect, shape = RoundedCornerShape(10.dp)) { Text("Sambung") }
            }
        }
    }
}
