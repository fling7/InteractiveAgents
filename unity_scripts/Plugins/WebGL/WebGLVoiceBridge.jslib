mergeInto(LibraryManager.library, {
  IAVoice_IsSupported: function () {
    return !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia && window.MediaRecorder) ? 1 : 0;
  },

  IAVoice_StartRecording: function (
    gameObjectNamePtr,
    successMethodPtr,
    errorMethodPtr,
    backendBaseUrlPtr,
    sttModelPtr,
    sttLanguagePtr,
    maxSeconds
  ) {
    var gameObjectName = UTF8ToString(gameObjectNamePtr);
    var successMethod = UTF8ToString(successMethodPtr);
    var errorMethod = UTF8ToString(errorMethodPtr);
    var backendBaseUrl = UTF8ToString(backendBaseUrlPtr || 0);
    var sttModel = UTF8ToString(sttModelPtr || 0);
    var sttLanguage = UTF8ToString(sttLanguagePtr || 0);

    function send(method, payload) {
      if (typeof SendMessage === "function") {
        SendMessage(gameObjectName, method, payload);
        return;
      }
      if (typeof Module !== "undefined" && Module && typeof Module.SendMessage === "function") {
        Module.SendMessage(gameObjectName, method, payload);
        return;
      }
      if (window.unityInstance && typeof window.unityInstance.SendMessage === "function") {
        window.unityInstance.SendMessage(gameObjectName, method, payload);
        return;
      }
      if (window.gameInstance && typeof window.gameInstance.SendMessage === "function") {
        window.gameInstance.SendMessage(gameObjectName, method, payload);
      }
    }

    function sendError(message) {
      send(errorMethod, message || "Browser-Mikrofonfehler.");
    }

    if (!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia && window.MediaRecorder)) {
      sendError("Browser unterstuetzt MediaRecorder/getUserMedia nicht.");
      return;
    }

    if (!backendBaseUrl) {
      sendError("Backend Base URL fehlt.");
      return;
    }

    var state = window.InteractiveAgentsVoiceBridge || {};
    if (state.recorder && state.recorder.state === "recording") {
      sendError("Aufnahme laeuft bereits.");
      return;
    }

    var trimmedBaseUrl = backendBaseUrl.replace(/\/+$/, "");
    state = {
      recorder: null,
      chunks: [],
      stream: null,
      timer: null,
      gameObjectName: gameObjectName,
      successMethod: successMethod,
      errorMethod: errorMethod,
      backendBaseUrl: trimmedBaseUrl,
      sttModel: sttModel,
      sttLanguage: sttLanguage,
      send: send,
      sendError: sendError
    };
    window.InteractiveAgentsVoiceBridge = state;

    navigator.mediaDevices.getUserMedia({ audio: true }).then(function (stream) {
      var options = {};
      if (MediaRecorder.isTypeSupported && MediaRecorder.isTypeSupported("audio/webm;codecs=opus")) {
        options.mimeType = "audio/webm;codecs=opus";
      } else if (MediaRecorder.isTypeSupported && MediaRecorder.isTypeSupported("audio/webm")) {
        options.mimeType = "audio/webm";
      }

      var recorder = new MediaRecorder(stream, options);
      state.stream = stream;
      state.recorder = recorder;
      state.chunks = [];

      recorder.ondataavailable = function (event) {
        if (event.data && event.data.size > 0) {
          state.chunks.push(event.data);
        }
      };

      recorder.onerror = function (event) {
        sendError(event.error ? event.error.message : "MediaRecorder Fehler.");
      };

      recorder.onstop = function () {
        if (state.timer) {
          clearTimeout(state.timer);
          state.timer = null;
        }
        if (state.stream) {
          state.stream.getTracks().forEach(function (track) { track.stop(); });
          state.stream = null;
        }

        var mimeType = recorder.mimeType || "audio/webm";
        var blob = new Blob(state.chunks || [], { type: mimeType });
        state.chunks = [];

        if (!blob.size) {
          sendError("Keine Audiodaten aufgenommen.");
          return;
        }

        var form = new FormData();
        form.append("audio", blob, "voice.webm");
        if (state.sttModel) {
          form.append("model", state.sttModel);
        }
        if (state.sttLanguage) {
          form.append("language", state.sttLanguage);
        }

        fetch(state.backendBaseUrl + "/stt", {
          method: "POST",
          body: form
        }).then(function (response) {
          if (!response.ok) {
            return response.text().then(function (text) {
              throw new Error(text || ("HTTP " + response.status));
            });
          }
          return response.json();
        }).then(function (json) {
          send(successMethod, JSON.stringify(json || {}));
        }).catch(function (error) {
          sendError(error && error.message ? error.message : "Transkription fehlgeschlagen.");
        });
      };

      recorder.start();

      var timeoutMs = Math.max(1, Number(maxSeconds) || 10) * 1000;
      state.timer = setTimeout(function () {
        if (state.recorder && state.recorder.state === "recording") {
          state.recorder.stop();
        }
      }, timeoutMs);
    }).catch(function (error) {
      sendError(error && error.message ? error.message : "Mikrofonzugriff verweigert.");
    });
  },

  IAVoice_StopRecording: function () {
    var state = window.InteractiveAgentsVoiceBridge;
    if (!state || !state.recorder) {
      return;
    }
    if (state.recorder.state === "recording") {
      state.recorder.stop();
    }
  }
});
