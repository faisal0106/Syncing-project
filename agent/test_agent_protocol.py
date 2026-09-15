import asyncio, json, sys, urllib.request
import websockets

TOKEN_URL = "http://127.0.0.1:8787/pairing-token"
WS_URL = "ws://127.0.0.1:8787/"

def fail(msg):
    print(f"FAIL: {msg}")
    sys.exit(1)

async def recv_typed(ws, expected_types, timeout=5):
    """Receive messages until one matching expected_types (list) arrives, skipping periodic SYNC/SESSION_STATE pushes if not expected."""
    while True:
        raw = await asyncio.wait_for(ws.recv(), timeout=timeout)
        msg = json.loads(raw)
        if msg["type"] in expected_types:
            return msg
        else:
            print(f"  (skipping unsolicited {msg['type']})")

async def main():
    print("== Fetching pairing token via HTTP ==")
    with urllib.request.urlopen(TOKEN_URL) as resp:
        body = json.loads(resp.read())
    token = body["token"]
    print(f"token = {token[:12]}...")
    if not token:
        fail("empty token")

    print("\n== Testing UNAUTHORIZED path (bad token) ==")
    async with websockets.connect(WS_URL) as ws:
        await ws.send(json.dumps({"type": "HELLO", "payload": {"token": "wrong", "clientVersion": "test"}}))
        msg = json.loads(await asyncio.wait_for(ws.recv(), timeout=5))
        assert msg["type"] == "ERROR" and msg["payload"]["code"] == "UNAUTHORIZED", msg
        print(f"  got expected ERROR: {msg['payload']}")

    print("\n== Testing bad Origin rejection ==")
    try:
        async with websockets.connect(WS_URL, additional_headers={"Origin": "https://evil.example.com"}) as ws:
            fail("connection should have been rejected for bad origin")
    except websockets.exceptions.InvalidStatus as e:
        print(f"  rejected as expected: {e}")

    print("\n== Full HELLO -> DEVICE_LIST -> CONNECT -> SESSION -> PLAY -> SYNC -> PAUSE -> STOP -> STATUS flow ==")
    async with websockets.connect(WS_URL) as ws:
        await ws.send(json.dumps({"type": "HELLO", "payload": {"token": token, "clientVersion": "test"}, "requestId": "r1"}))
        msg = await recv_typed(ws, ["HELLO_ACK"])
        assert msg["requestId"] == "r1", msg
        print(f"  HELLO_ACK: {msg['payload']}")

        await ws.send(json.dumps({"type": "DEVICE_LIST", "payload": {}}))
        msg = await recv_typed(ws, ["DEVICE_LIST_RESULT"])
        devices = msg["payload"]["devices"]
        assert devices, "No active Windows audio output devices were found"
        for d in devices:
            assert d["state"] in ("available", "connected", "playing", "paused", "disconnected"), d
        print(f"  devices: {[ (d['id'], d['type'], d['state']) for d in devices ]}")
        device_ids = [
            d["id"] for d in devices
            if d["state"] in ("available", "connected", "playing", "paused")
        ]
        assert device_ids, "No active Windows audio output devices are available"

        for did in device_ids:
            await ws.send(json.dumps({"type": "DEVICE_CONNECT", "payload": {"deviceId": did}}))
            msg = await recv_typed(ws, ["DEVICE_STATE"])
            assert msg["payload"]["state"] == "connected", msg
            print(f"  connected {did}: {msg['payload']}")

        # Bad device id -> DEVICE_NOT_FOUND
        await ws.send(json.dumps({"type": "DEVICE_CONNECT", "payload": {"deviceId": "no-such-device"}}))
        msg = await recv_typed(ws, ["ERROR"])
        assert msg["payload"]["code"] == "DEVICE_NOT_FOUND", msg
        print(f"  DEVICE_NOT_FOUND handled: {msg['payload']}")

        await ws.send(json.dumps({"type": "CREATE_SESSION", "payload": {"name": "test-session", "deviceIds": device_ids}}))
        msg = await recv_typed(ws, ["SESSION_STATE"])
        session_id = msg["sessionId"]
        assert msg["payload"]["playbackState"] == "stopped", msg
        assert len(msg["payload"]["devices"]) == len(device_ids), msg
        print(f"  session created: {session_id}, state={msg['payload']}")

        await ws.send(json.dumps({"type": "PLAY", "sessionId": session_id, "payload": {"position": 0, "targetTimestamp": 0}}))
        msg = await recv_typed(ws, ["SESSION_STATE"])
        assert msg["payload"]["playbackState"] == "playing", msg
        print(f"  after PLAY: {msg['payload']}")

        print("  waiting for a periodic SYNC push...")
        msg = await recv_typed(ws, ["SYNC"], timeout=3)
        assert msg["sessionId"] == session_id
        for d in msg["payload"]["devices"]:
            assert d["syncState"] in ("synced", "syncing", "degraded"), d
            assert isinstance(d["driftEstimateMsPerSec"], (int, float))
        print(f"  SYNC: {msg['payload']['devices']}")

        await ws.send(json.dumps({"type": "SET_VOLUME", "sessionId": session_id, "payload": {"volume": 0.5}}))
        msg = await recv_typed(ws, ["SESSION_STATE"])
        assert msg["payload"]["volume"] == 0.5, msg
        print(f"  after SET_VOLUME: volume={msg['payload']['volume']}")

        await ws.send(json.dumps({"type": "SET_DEVICE_ENABLED", "sessionId": session_id, "payload": {"deviceId": device_ids[0], "enabled": False}}))
        msg = await recv_typed(ws, ["SESSION_STATE"])
        print(f"  after disabling {device_ids[0]}: {msg['payload']}")

        await ws.send(json.dumps({"type": "PAUSE", "sessionId": session_id, "payload": {}}))
        msg = await recv_typed(ws, ["SESSION_STATE"])
        assert msg["payload"]["playbackState"] == "paused", msg
        print(f"  after PAUSE: {msg['payload']['playbackState']}")

        await ws.send(json.dumps({"type": "STOP", "sessionId": session_id, "payload": {}}))
        msg = await recv_typed(ws, ["SESSION_STATE"])
        assert msg["payload"]["playbackState"] == "stopped", msg
        print(f"  after STOP: {msg['payload']['playbackState']}")

        # Unknown session -> SESSION_NOT_FOUND
        await ws.send(json.dumps({"type": "PLAY", "sessionId": "bogus", "payload": {"position": 0, "targetTimestamp": 0}}))
        msg = await recv_typed(ws, ["ERROR"])
        assert msg["payload"]["code"] == "SESSION_NOT_FOUND", msg
        print(f"  SESSION_NOT_FOUND handled: {msg['payload']}")

        await ws.send(json.dumps({"type": "GET_STATUS", "payload": {}}))
        msg = await recv_typed(ws, ["STATUS"])
        print(f"  STATUS: {msg['payload']}")
        assert msg["payload"]["activeSessionId"] == session_id, msg

    print("\nALL CHECKS PASSED")

asyncio.run(main())
