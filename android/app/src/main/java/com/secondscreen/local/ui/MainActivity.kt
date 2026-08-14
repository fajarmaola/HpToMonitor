package com.secondscreen.local.ui

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
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
        I18n.init(applicationContext)
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

        // --- In-app update (GitHub Releases) ---
        val ctx = LocalContext.current
        var updBusy by remember { mutableStateOf(false) }
        var updInfo by remember { mutableStateOf<ReleaseInfo?>(null) }
        fun checkUpdate() {
            scope.launch {
                updBusy = true
                val info = Updater.check(BuildConfig.VERSION_NAME)
                updBusy = false
                if (info.available) updInfo = info
                else Toast.makeText(ctx,
                    if (info.message.isNotEmpty()) info.message else I18n.t("upd.uptodate"),
                    Toast.LENGTH_LONG).show()
            }
        }

        Surface(Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
            if (state == ClientState.Streaming) {
                ConnectedInstructions(onStart = {
                    startActivity(Intent(this@MainActivity, MonitorActivity::class.java))
                })
            } else {
                Column(Modifier.fillMaxSize().padding(24.dp)) {
                    // Header with language toggle
                    Row(verticalAlignment = Alignment.Top) {
                        Column(Modifier.weight(1f)) {
                            Text(I18n.t("app.title"), fontSize = 22.sp, fontWeight = FontWeight.Bold,
                                color = MaterialTheme.colorScheme.onBackground)
                            Text(I18n.t("app.tagline"), fontSize = 13.sp,
                                color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.6f))
                        }
                        OutlinedButton(onClick = { I18n.toggle() },
                            shape = RoundedCornerShape(10.dp),
                            contentPadding = PaddingValues(horizontal = 14.dp, vertical = 6.dp)) {
                            Text(I18n.toggleLabel(), fontWeight = FontWeight.SemiBold)
                        }
                    }

                    TextButton(onClick = { checkUpdate() }, enabled = !updBusy) {
                        Text(if (updBusy) I18n.t("upd.checking") else I18n.t("btn.update"),
                            fontSize = 12.sp)
                    }

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
                        shape = RoundedCornerShape(14.dp),
                        modifier = Modifier.fillMaxWidth().height(52.dp)
                    ) { Text(if (searching) I18n.t("btn.searching") else I18n.t("btn.search"),
                        fontSize = 15.sp, fontWeight = FontWeight.SemiBold) }

                    Spacer(Modifier.height(18.dp))
                    Text(I18n.t("lbl.availablePcs"), fontSize = 11.sp, fontWeight = FontWeight.Bold,
                        color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.6f))
                    Spacer(Modifier.height(8.dp))

                    LazyColumn(Modifier.weight(1f)) {
                        items(hosts) { host ->
                            HostRow(host) { connection.connect(host) }
                        }
                        if (hosts.isEmpty() && !searching) {
                            item {
                                Text(I18n.t("empty.noPcs"), fontSize = 13.sp,
                                    color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.5f),
                                    modifier = Modifier.padding(top = 12.dp))
                            }
                        }
                    }

                    error?.let {
                        Text(it, color = MaterialTheme.colorScheme.error, fontSize = 13.sp,
                            modifier = Modifier.padding(top = 8.dp))
                    }

                    Text(I18n.t("footer"), fontSize = 11.sp,
                        color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.4f),
                        modifier = Modifier.fillMaxWidth().padding(top = 10.dp),
                        textAlign = TextAlign.Center)
                }
            }
        }

        if (needPin) {
            AlertDialog(
                onDismissRequest = { },
                title = { Text(I18n.t("dlg.pinTitle")) },
                text = {
                    Column {
                        Text(I18n.t("dlg.pinBody"))
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
                    ) { Text(I18n.t("btn.connect")) }
                },
                dismissButton = {
                    TextButton(onClick = { connection.disconnect() }) { Text(I18n.t("btn.cancel")) }
                }
            )
        }

        updInfo?.let { info ->
            AlertDialog(
                onDismissRequest = { updInfo = null },
                title = { Text(I18n.t("upd.available")) },
                text = {
                    Column {
                        Text("v${info.latestVersion}  (${I18n.t("state.ready")}: v${info.currentVersion})")
                        if (info.notes.isNotEmpty()) {
                            Spacer(Modifier.height(8.dp))
                            Text(info.notes, fontSize = 13.sp)
                        }
                    }
                },
                confirmButton = {
                    TextButton(onClick = {
                        val url = info.apkUrl
                        updInfo = null
                        if (url != null) scope.launch {
                            updBusy = true
                            Toast.makeText(ctx, I18n.t("upd.downloading"), Toast.LENGTH_SHORT).show()
                            val f = Updater.download(ctx, url)
                            updBusy = false
                            if (f != null) Updater.install(ctx, f)
                            else Toast.makeText(ctx, I18n.t("upd.failed"), Toast.LENGTH_LONG).show()
                        }
                    }) { Text(I18n.t("upd.now")) }
                },
                dismissButton = {
                    TextButton(onClick = { updInfo = null }) { Text(I18n.t("upd.later")) }
                }
            )
        }
    }

    // Modern success + instruction screen shown right after a connection is established.
    @Composable
    private fun ConnectedInstructions(onStart: () -> Unit) {
        Column(
            Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(28.dp),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            Spacer(Modifier.height(24.dp))
            Box(
                Modifier.size(96.dp).background(MaterialTheme.colorScheme.primary.copy(alpha = 0.15f), CircleShape),
                contentAlignment = Alignment.Center
            ) {
                Text("✓", fontSize = 52.sp, fontWeight = FontWeight.Bold,
                    color = MaterialTheme.colorScheme.primary)
            }
            Spacer(Modifier.height(18.dp))
            Text(I18n.t("conn.title"), fontSize = 26.sp, fontWeight = FontWeight.Bold,
                color = MaterialTheme.colorScheme.onBackground, textAlign = TextAlign.Center)
            Spacer(Modifier.height(6.dp))
            Text(I18n.t("conn.subtitle"), fontSize = 14.sp,
                color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.7f), textAlign = TextAlign.Center)

            Spacer(Modifier.height(28.dp))
            StepCard(1, I18n.t("conn.step1"))
            Spacer(Modifier.height(12.dp))
            StepCard(2, I18n.t("conn.step2"))
            Spacer(Modifier.height(12.dp))
            StepCard(3, I18n.t("conn.step3"))

            Spacer(Modifier.height(28.dp))
            Button(onClick = onStart, shape = RoundedCornerShape(16.dp),
                modifier = Modifier.fillMaxWidth().height(56.dp)) {
                Text(I18n.t("conn.start"), fontSize = 16.sp, fontWeight = FontWeight.Bold)
            }
            Spacer(Modifier.height(18.dp))
            Text(I18n.t("footer"), fontSize = 11.sp,
                color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.4f), textAlign = TextAlign.Center)
        }
    }

    @Composable
    private fun StepCard(number: Int, text: String) {
        Surface(
            color = MaterialTheme.colorScheme.surface,
            shape = RoundedCornerShape(16.dp),
            modifier = Modifier.fillMaxWidth()
        ) {
            Row(Modifier.padding(16.dp), verticalAlignment = Alignment.CenterVertically) {
                Box(
                    Modifier.size(34.dp).background(MaterialTheme.colorScheme.primary, CircleShape),
                    contentAlignment = Alignment.Center
                ) {
                    Text("$number", color = MaterialTheme.colorScheme.onPrimary,
                        fontWeight = FontWeight.Bold, fontSize = 16.sp)
                }
                Spacer(Modifier.width(14.dp))
                Text(text, fontSize = 14.sp, color = MaterialTheme.colorScheme.onSurface,
                    modifier = Modifier.weight(1f))
            }
        }
    }

    @Composable
    private fun StatusPill(state: ClientState) {
        val label = when (state) {
            ClientState.Idle -> I18n.t("state.ready")
            ClientState.Discovering -> I18n.t("state.searching")
            ClientState.Connecting -> I18n.t("state.connecting")
            ClientState.Pairing, ClientState.AwaitingPin -> I18n.t("state.pairing")
            ClientState.Configuring -> I18n.t("state.configuring")
            ClientState.Streaming -> I18n.t("state.connected")
            ClientState.Reconnecting -> I18n.t("state.reconnecting")
            ClientState.Disconnected -> I18n.t("state.disconnected")
            ClientState.Error -> I18n.t("state.error")
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
            shape = RoundedCornerShape(14.dp),
            modifier = Modifier.fillMaxWidth().padding(vertical = 6.dp)
        ) {
            Row(Modifier.padding(16.dp), verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) {
                    Text(host.name, fontSize = 16.sp, fontWeight = FontWeight.SemiBold,
                        color = MaterialTheme.colorScheme.onSurface)
                    Text(host.ip, fontSize = 12.sp,
                        color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.6f))
                }
                Button(onClick = onConnect, shape = RoundedCornerShape(10.dp)) { Text(I18n.t("btn.connect")) }
            }
        }
    }
}
