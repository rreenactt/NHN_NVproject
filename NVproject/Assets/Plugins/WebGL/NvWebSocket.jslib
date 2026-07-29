// 브라우저 WebSocket 을 C# 에서 폴링으로 쓰기 위한 최소 래퍼.
//
// Socket.IO 나 SignalR 을 쓰지 않는다. 서버는 raw WebSocket 이고
// 프레임은 수기 비트패커로 만든 바이너리다.
//
// WebGL 은 싱글 스레드다. 수신은 콜백이 큐에 넣고 게임 루프가 꺼내 간다.
// C# 쪽에서 블로킹하거나 콜백을 기다리지 않는다.

var NvWebSocketLibrary = {

  $NvWs: {
    socket: null,
    queue: [],

    // 0 닫힘, 1 연결 중, 2 열림, 3 오류
    state: 0,

    // 큐가 무한히 자라면 탭을 백그라운드로 두었을 때 메모리를 먹는다.
    // 스냅샷은 유실해도 다음 틱이 대체하므로 오래된 것을 버린다.
    maxQueued: 64,

    closeCode: 0,
  },

  // url 은 wss:// 여야 한다. HTTPS 페이지에서 ws:// 는 mixed content 로 차단된다.
  NvWsOpen: function (urlPointer) {
    var url = UTF8ToString(urlPointer);

    if (NvWs.socket !== null) {
      return;
    }

    NvWs.queue.length = 0;
    NvWs.closeCode = 0;
    NvWs.state = 1;

    try {
      NvWs.socket = new WebSocket(url);
    } catch (error) {
      NvWs.state = 3;
      NvWs.socket = null;
      return;
    }

    // 이 설정이 없으면 Blob 으로 수신되어 동기 읽기가 불가능하다.
    NvWs.socket.binaryType = 'arraybuffer';

    NvWs.socket.onopen = function () {
      NvWs.state = 2;
    };

    NvWs.socket.onmessage = function (event) {
      if (typeof event.data === 'string') {
        return;
      }

      if (NvWs.queue.length >= NvWs.maxQueued) {
        NvWs.queue.shift();
      }

      NvWs.queue.push(new Uint8Array(event.data));
    };

    NvWs.socket.onerror = function () {
      NvWs.state = 3;
    };

    NvWs.socket.onclose = function (event) {
      NvWs.state = 0;
      NvWs.closeCode = event.code;
      NvWs.socket = null;
    };
  },

  NvWsState: function () {
    return NvWs.state;
  },

  NvWsCloseCode: function () {
    return NvWs.closeCode;
  },

  // 보냈으면 1, 못 보냈으면 0.
  NvWsSend: function (dataPointer, length) {
    if (NvWs.socket === null || NvWs.state !== 2) {
      return 0;
    }

    try {
      NvWs.socket.send(HEAPU8.subarray(dataPointer, dataPointer + length));
      return 1;
    } catch (error) {
      return 0;
    }
  },

  // 다음 메시지의 바이트 수. 없으면 0.
  NvWsPeekSize: function () {
    return NvWs.queue.length === 0 ? 0 : NvWs.queue[0].length;
  },

  // 다음 메시지를 복사하고 바이트 수를 반환한다. 공간이 부족하면 버리고 -1.
  NvWsReceive: function (destinationPointer, capacity) {
    if (NvWs.queue.length === 0) {
      return 0;
    }

    var message = NvWs.queue[0];

    if (message.length > capacity) {
      NvWs.queue.shift();
      return -1;
    }

    HEAPU8.set(message, destinationPointer);
    NvWs.queue.shift();
    return message.length;
  },

  NvWsClose: function () {
    if (NvWs.socket === null) {
      return;
    }

    try {
      NvWs.socket.close();
    } catch (error) {
      // 이미 닫히는 중이면 무시한다.
    }

    NvWs.socket = null;
    NvWs.state = 0;
    NvWs.queue.length = 0;
  },
};

autoAddDeps(NvWebSocketLibrary, '$NvWs');
mergeInto(LibraryManager.library, NvWebSocketLibrary);
