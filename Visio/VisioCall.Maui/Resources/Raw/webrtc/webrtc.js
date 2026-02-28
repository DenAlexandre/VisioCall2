// WebRTC logic — communicates with C# via visiocall:// URL scheme

let pc = null;
let localStream = null;
const statusEl = document.getElementById('status');
const remoteVideo = document.getElementById('remoteVideo');
const localVideo = document.getElementById('localVideo');

const config = {
    iceServers: [
        { urls: 'stun:stun.l.google.com:19302' },
        { urls: 'stun:stun1.l.google.com:19302' }
    ]
};

// --- JS → C# bridge ---
// Windows (WebView2): messages queued, C# polls via flushMessages()
// Android/iOS: messages sent via iframe URL interception
const _outbox = [];
const _isWebView2 = !!(window.chrome && window.chrome.webview);

function sendToNative(action, data) {
    if (_isWebView2) {
        _outbox.push({ action: action, data: data });
    } else {
        const encoded = encodeURIComponent(JSON.stringify(data));
        const url = 'visiocall://' + action + '/' + encoded;
        const iframe = document.createElement('iframe');
        iframe.style.display = 'none';
        iframe.src = url;
        document.body.appendChild(iframe);
        setTimeout(() => iframe.remove(), 100);
    }
}

// Called by C# polling to collect pending messages.
// Returns the raw array — EvaluateJavaScriptAsync handles JSON serialization.
function flushMessages() {
    if (_outbox.length === 0) return null;
    return _outbox.splice(0);
}

function log(msg) {
    statusEl.textContent = msg;
}

// --- Wait for ICE gathering to complete ---
function waitForIceGathering() {
    return new Promise(resolve => {
        if (pc.iceGatheringState === 'complete') {
            resolve();
            return;
        }
        const check = () => {
            if (pc.iceGatheringState === 'complete') {
                pc.removeEventListener('icegatheringstatechange', check);
                resolve();
            }
        };
        pc.addEventListener('icegatheringstatechange', check);
        setTimeout(resolve, 5000);
    });
}

// --- Media ---
async function initMedia() {
    try {
        log('Requesting camera...');
        localStream = await navigator.mediaDevices.getUserMedia({
            video: { facingMode: 'user', width: { ideal: 640 }, height: { ideal: 480 } },
            audio: true
        });
        localVideo.srcObject = localStream;
        log('Camera ready');
        return true;
    } catch (err) {
        log('Camera error: ' + err.message);
        sendToNative('error', 'getUserMedia failed: ' + err.message);
        return false;
    }
}

// --- Peer Connection ---
function createPeerConnection() {
    pc = new RTCPeerConnection(config);

    if (localStream) {
        localStream.getTracks().forEach(track => pc.addTrack(track, localStream));
    }

    pc.ontrack = (event) => {
        if (event.streams && event.streams[0]) {
            remoteVideo.srcObject = event.streams[0];
        } else {
            let stream = remoteVideo.srcObject;
            if (!stream) {
                stream = new MediaStream();
                remoteVideo.srcObject = stream;
            }
            stream.addTrack(event.track);
        }
        setTimeout(() => { statusEl.textContent = ''; }, 500);
    };

    pc.onicecandidate = (event) => {
        if (event.candidate) {
            log('Gathering ICE...');
        }
    };

    pc.oniceconnectionstatechange = () => {
        const state = pc.iceConnectionState;
        if (state === 'connected' || state === 'completed') {
            statusEl.textContent = '';
        } else if (state === 'failed') {
            log('Connection failed');
        }
    };

    pc.onconnectionstatechange = () => {
        const state = pc.connectionState;
        if (state === 'connecting') {
            log('Connecting...');
        } else if (state === 'connected') {
            statusEl.textContent = '';
        }
    };
}

// --- Caller: create offer (vanilla ICE) ---
async function createOffer() {
    const ready = await initMedia();
    if (!ready) return;

    createPeerConnection();
    log('Creating offer...');

    try {
        const offer = await pc.createOffer();
        await pc.setLocalDescription(offer);
        log('Gathering ICE candidates...');
        await waitForIceGathering();

        const complete = pc.localDescription;
        log('Sending offer...');
        sendToNative('offer', { type: complete.type, sdp: complete.sdp });
    } catch (err) {
        log('Offer error: ' + err.message);
        sendToNative('error', 'createOffer failed: ' + err.message);
    }
}

// --- Callee: receive offer then auto-answer (vanilla ICE) ---
async function receiveOffer(offer) {
    log('Received offer');
    const ready = await initMedia();
    if (!ready) return;

    createPeerConnection();

    try {
        await pc.setRemoteDescription(new RTCSessionDescription({
            type: offer.type,
            sdp: offer.sdp
        }));
        log('Creating answer...');

        const answer = await pc.createAnswer();
        await pc.setLocalDescription(answer);
        log('Gathering ICE candidates...');
        await waitForIceGathering();

        const complete = pc.localDescription;
        log('Sending answer...');
        sendToNative('answer', { type: complete.type, sdp: complete.sdp });
    } catch (err) {
        log('Answer error: ' + err.message);
        sendToNative('error', 'receiveOffer failed: ' + err.message);
    }
}

// --- Caller: receive answer ---
async function receiveAnswer(answer) {
    if (!pc) { log('receiveAnswer: no pc!'); return; }
    try {
        await pc.setRemoteDescription(new RTCSessionDescription({
            type: answer.type,
            sdp: answer.sdp
        }));
        log('Connecting...');
    } catch (err) {
        log('receiveAnswer error: ' + err.message);
        sendToNative('error', 'receiveAnswer failed: ' + err.message);
    }
}

// --- Both: add ICE candidate ---
async function receiveIceCandidate(candidate) {
    if (!pc) return;
    try {
        await pc.addIceCandidate(new RTCIceCandidate({
            candidate: candidate.candidate,
            sdpMid: candidate.sdpMid,
            sdpMLineIndex: candidate.sdpMLineIndex
        }));
    } catch (err) {
        // Ignore — vanilla ICE candidates are already in the SDP
    }
}

function toggleMute() {
    if (!localStream) return;
    const audioTrack = localStream.getAudioTracks()[0];
    if (audioTrack) audioTrack.enabled = !audioTrack.enabled;
}

function toggleCamera() {
    if (!localStream) return;
    const videoTrack = localStream.getVideoTracks()[0];
    if (videoTrack) videoTrack.enabled = !videoTrack.enabled;
}

function closeConnection() {
    if (pc) { pc.close(); pc = null; }
    if (localStream) {
        localStream.getTracks().forEach(track => track.stop());
        localStream = null;
    }
    remoteVideo.srcObject = null;
    localVideo.srcObject = null;
}
