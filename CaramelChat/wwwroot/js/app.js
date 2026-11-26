(() => {
  "use strict";

  const state = {
    me: null,
    peers: [],
    conversations: [],
    calls: [],
    blocks: [],
    activePeerId: null,
    messages: [],
    filter: "",
    filterMode: "all",
    view: "chats",
    socket: null,
    socketOpen: false,
    recording: null,
    renameTarget: null,
    typing: {},
    avatarUploading: false,
    search: { query: "", results: [], index: 0, active: false, timer: null },
    forward: { messageId: null, targets: new Set() },
    shared: { tab: "media", data: { media: [], documents: [], links: [] } },
    camera: { stream: null, facing: "environment" },
    pollDraft: [],
    viewOnce: false,
    messageMenu: { messageId: null }
  };

  const statusState = {
    mine: [],
    peers: [],
    tab: "text",
    background: "#0A7C5A",
    file: null,
    previewUrl: null,
    durationMs: 0,
    viewer: {
      owner: null,
      items: [],
      index: 0,
      mine: false,
      timer: null
    },
    privacyPeers: new Set(),
    liked: new Set(JSON.parse(localStorage.getItem("whatsup.likes.v1") || "[]"))
  };

  const STATUS_COLORS = ["#0A7C5A", "#1F6E8C", "#6A4C93", "#B33F62", "#C47F00", "#3B4A54"];

  // Traduction hors ligne (Argos) : les traductions déjà obtenues sont gardées en
  // mémoire pour l'affichage, le cache persistant étant tenu par le serveur.
  const translationState = {
    settings: { sourceLang: "fr", targetLang: "en", languages: [] },
    status: null,
    models: null,
    cache: { count: 0, characters: 0 },
    messages: new Map(),
    showAll: false,
    pending: null,
    tab: "installed",
    installJob: null,
    pollTimer: null,
    loadingModels: false
  };

  let lastBadgeCount = -1;
  const notifiedMessages = new Set();

  const $ = (id) => document.getElementById(id);

  const els = {
    app: document.querySelector(".app"),
    myAvatar: $("myAvatar"),
    myName: $("myName"),
    onlineBadge: $("onlineBadge"),
    editNameBtn: $("editNameBtn"),
    searchInput: $("searchInput"),
    peerCount: $("peerCount"),
    peerList: $("peerList"),
    conversationList: $("conversationList"),
    footStatus: $("footStatus"),
    welcome: $("welcome"),
    welcomeFoot: $("welcomeFoot"),
    viewOnceToggle: $("viewOnceToggle"),
    viewOnceSwitch: $("viewOnceSwitch"),
    viewOnceViewer: $("viewOnceViewer"),
    viewOnceStage: $("viewOnceStage"),
    viewOnceClose: $("viewOnceClose"),
    chatPanel: $("chatPanel"),
    backBtn: $("backBtn"),
    peerAvatar: $("peerAvatar"),
    peerTitle: $("peerTitle"),
    peerSubtitle: $("peerSubtitle"),
    contactMenuBtn: $("contactMenuBtn"),
    audioCallBtn: $("audioCallBtn"),
    videoCallBtn: $("videoCallBtn"),
    messages: $("messages"),
    messageInput: $("messageInput"),
    sendBtn: $("sendBtn"),
    attachBtn: $("attachBtn"),
    attachMenu: $("attachMenu"),
    imageInput: $("imageInput"),
    videoInput: $("videoInput"),
    fileInput: $("fileInput"),
    audioInput: $("audioInput"),
    // Profil, thème et menus
    chatsMenuBtn: $("chatsMenuBtn"),
    chatsMenu: $("chatsMenu"),
    profileModal: $("profileModal"),
    profileClose: $("profileClose"),
    profileAvatar: $("profileAvatar"),
    profileName: $("profileName"),
    profileChangeAvatar: $("profileChangeAvatar"),
    profileRename: $("profileRename"),
    themeSwitch: $("themeSwitch"),
    sourceLangSelect: $("sourceLangSelect"),
    targetLangSelect: $("targetLangSelect"),
    translationState: $("translationState"),
    translationEngineHint: $("translationEngineHint"),
    translationCacheHint: $("translationCacheHint"),
    openLanguagesBtn: $("openLanguagesBtn"),
    clearTranslationCacheBtn: $("clearTranslationCacheBtn"),
    translationModal: $("translationModal"),
    translationClose: $("translationClose"),
    translationEngine: $("translationEngine"),
    translationEngineText: $("translationEngineText"),
    argosPathInput: $("argosPathInput"),
    pythonPathInput: $("pythonPathInput"),
    saveTranslationPathsBtn: $("saveTranslationPathsBtn"),
    translationProgress: $("translationProgress"),
    translationProgressLabel: $("translationProgressLabel"),
    translationProgressValue: $("translationProgressValue"),
    translationBar: $("translationBar"),
    translationCancelBtn: $("translationCancelBtn"),
    translationBody: $("translationBody"),
    translationNote: $("translationNote"),
    translatePreviewModal: $("translatePreviewModal"),
    translatePreviewLangs: $("translatePreviewLangs"),
    translateOriginal: $("translateOriginal"),
    translateText: $("translateText"),
    translateRestore: $("translateRestore"),
    translateCancel: $("translateCancel"),
    translateSend: $("translateSend"),
    chatTranslateBtn: $("chatTranslateBtn"),
    composerTranslateBtn: $("composerTranslateBtn"),
    messageMenu: $("messageMenu"),
    peerProfileModal: $("peerProfileModal"),
    peerProfileClose: $("peerProfileClose"),
    peerProfileAvatar: $("peerProfileAvatar"),
    peerProfileName: $("peerProfileName"),
    peerProfileMeta: $("peerProfileMeta"),
    peerProfileMessage: $("peerProfileMessage"),
    peerProfileCall: $("peerProfileCall"),
    peerProfileVideo: $("peerProfileVideo"),
    peerProfileRename: $("peerProfileRename"),
    // Menu du contact
    menuViewProfile: $("menuViewProfile"),
    menuShared: $("menuShared"),
    menuClear: $("menuClear"),
    menuDelete: $("menuDelete"),
    // Transfert
    forwardModal: $("forwardModal"),
    forwardList: $("forwardList"),
    forwardCancel: $("forwardCancel"),
    forwardSend: $("forwardSend"),
    // Médias / liens / documents
    sharedModal: $("sharedModal"),
    sharedTabs: $("sharedTabs"),
    sharedBody: $("sharedBody"),
    sharedClose: $("sharedClose"),
    // Caméra / sondage
    cameraModal: $("cameraModal"),
    cameraVideo: $("cameraVideo"),
    cameraCancel: $("cameraCancel"),
    cameraSwitch: $("cameraSwitch"),
    cameraCapture: $("cameraCapture"),
    pollModal: $("pollModal"),
    pollQuestion: $("pollQuestion"),
    pollOptionsEdit: $("pollOptionsEdit"),
    pollAddOption: $("pollAddOption"),
    pollCancel: $("pollCancel"),
    pollSend: $("pollSend"),
    statusPrivacyEditWrap: $("statusPrivacyEditWrap"),
    statusPrivacyEdit: $("statusPrivacyEdit"),
    statusPrivacyEditPeers: $("statusPrivacyEditPeers"),
    statusPrivacySave: $("statusPrivacySave"),
    // Statuts
    statusLikeBtn: $("statusLikeBtn"),
    statusLikeCount: $("statusLikeCount"),
    statusCommentInput: $("statusCommentInput"),
    statusCommentSend: $("statusCommentSend"),
    statusPauseTop: $("statusPauseTop"),
    statusReactRow: $("statusReactRow"),
    statusOwnNote: $("statusOwnNote"),
    statusOwnViews: $("statusOwnViews"),
    statusOpenViewersTop: $("statusOpenViewersTop"),
    statusLikesList: $("statusLikesList"),
    statusLikesCount: $("statusLikesCount"),
    statusCommentsList: $("statusCommentsList"),
    statusCommentsCount: $("statusCommentsCount"),
    statusViewersCount: $("statusViewersCount"),
    statusPrivacy: $("statusPrivacy"),
    statusPrivacyPeers: $("statusPrivacyPeers"),
    // Appels
    clearCallsBtn: $("clearCallsBtn"),
    recordBtn: $("recordBtn"),
    recordBar: $("recordBar"),
    recordTime: $("recordTime"),
    stopRecordBtn: $("stopRecordBtn"),
    cancelRecordBtn: $("cancelRecordBtn"),
    sizeHint: $("sizeHint"),
    blockedBanner: $("blockedBanner"),
    unblockChatBtn: $("unblockChatBtn"),
    secureHint: $("secureHint"),
    modal: $("modal"),
    modalTitle: $("modalTitle"),
    modalText: $("modalText"),
    modalInput: $("modalInput"),
    modalSave: $("modalSave"),
    modalCancel: $("modalCancel"),
    toastWrap: $("toastWrap"),
    avatarInput: $("avatarInput"),
    callLayer: $("callLayer"),
    callRemoteVideo: $("callRemoteVideo"),
    callLocalVideo: $("callLocalVideo"),
    callIdentity: $("callIdentity"),
    callAvatar: $("callAvatar"),
    callName: $("callName"),
    callStatus: $("callStatus"),
    callMuteBtn: $("callMuteBtn"),
    callCamBtn: $("callCamBtn"),
    callAcceptBtn: $("callAcceptBtn"),
    callHangupBtn: $("callHangupBtn"),
    chatsView: $("chatsView"),
    callsView: $("callsView"),
    statusesView: $("statusesView"),
    contactsView: $("contactsView"),
    callsList: $("callsList"),
    callsEmpty: $("callsEmpty"),
    callsSearch: $("callsSearch"),
    contactsSearch: $("contactsSearch"),
    chatFilters: $("chatFilters"),
    newChatBtn: $("newChatBtn"),
    callsStartBtn: $("callsStartBtn"),
    heroAudioBtn: $("heroAudioBtn"),
    heroVideoBtn: $("heroVideoBtn"),
    callsHero: $("callsHero"),
    contactsHero: $("contactsHero"),
    statusesHero: $("statusesHero"),
    callsSecureNote: $("callsSecureNote"),
    railUnread: $("railUnread"),
    railStatus: $("railStatus"),
    myStatusRow: $("myStatusRow"),
    myStatusLine: $("myStatusLine"),
    myStatusAdd: $("myStatusAdd"),
    myStatusAvatar: $("myStatusAvatar"),
    newStatusBtn: $("newStatusBtn"),
    heroStatusBtn: $("heroStatusBtn"),
    statusList: $("statusList"),
    statusViewedList: $("statusViewedList"),
    statusViewedWrap: $("statusViewedWrap"),
    statusEmpty: $("statusEmpty"),
    statusCount: $("statusCount"),
    blockedWrap: $("blockedWrap"),
    blockedList: $("blockedList"),
    blockedCount: $("blockedCount"),
    statusComposer: $("statusComposer"),
    statusComposeClose: $("statusComposeClose"),
    statusPublish: $("statusPublish"),
    statusTabs: $("statusTabs"),
    statusTextPreview: $("statusTextPreview"),
    statusMediaPreview: $("statusMediaPreview"),
    statusTextInput: $("statusTextInput"),
    statusCaption: $("statusCaption"),
    statusColors: $("statusColors"),
    statusFileInput: $("statusFileInput"),
    statusPickFile: $("statusPickFile"),
    statusImagePreview: $("statusImagePreview"),
    statusVideoPreview: $("statusVideoPreview"),
    statusViewer: $("statusViewer"),
    statusProgress: $("statusProgress"),
    statusViewerAvatar: $("statusViewerAvatar"),
    statusViewerName: $("statusViewerName"),
    statusViewerTime: $("statusViewerTime"),
    statusViewerClose: $("statusViewerClose"),
    statusContent: $("statusContent"),
    statusPrev: $("statusPrev"),
    statusNext: $("statusNext"),
    statusViewerCaption: $("statusViewerCaption"),
    statusViewsBtn: $("statusViewsBtn"),
    statusViewerViews: $("statusViewerViews"),
    statusViewersPanel: $("statusViewersPanel"),
    statusViewersList: $("statusViewersList"),
    statusViewersClose: $("statusViewersClose"),
    contactMenu: $("contactMenu"),
    menuAvatar: $("menuAvatar"),
    menuName: $("menuName"),
    menuRename: $("menuRename"),
    menuBlockChat: $("menuBlockChat"),
    menuBlockStatus: $("menuBlockStatus"),
    menuClose: $("menuClose"),
    switchChat: $("switchChat"),
    switchStatus: $("switchStatus"),
    // Application de bureau
    winControls: $("winControls"),
    // Recherche dans la conversation
    chatSearchBtn: $("chatSearchBtn"),
    chatSearchBar: $("chatSearchBar"),
    chatSearchInput: $("chatSearchInput"),
    chatSearchCount: $("chatSearchCount"),
    chatSearchPrev: $("chatSearchPrev"),
    chatSearchNext: $("chatSearchNext"),
    chatSearchClose: $("chatSearchClose"),
    unreadFilterLabel: $("unreadFilterLabel"),
    // Visualiseur d'image
    imageViewer: $("imageViewer"),
    viewerStage: $("viewerStage"),
    viewerImage: $("viewerImage"),
    viewerTitle: $("viewerTitle"),
    viewerTime: $("viewerTime"),
    viewerOpen: $("viewerOpen"),
    viewerDownload: $("viewerDownload"),
    viewerClose: $("viewerClose")
  };

  // ---------------------------------------------------------------- Helpers

  async function api(path, options = {}) {
    const config = { ...options, headers: { ...(options.headers || {}) } };
    if (config.body && !(config.body instanceof FormData)) {
      config.headers["Content-Type"] = "application/json";
    }

    const response = await fetch(path, config);
    const raw = await response.text();
    let data = null;
    if (raw) {
      try { data = JSON.parse(raw); } catch { data = raw; }
    }

    if (!response.ok) {
      const message = data && data.error ? data.error : `Erreur ${response.status}`;
      throw new Error(message);
    }
    return data;
  }

  function escapeHtml(value) {
    return String(value ?? "")
      .replaceAll("&", "&amp;")
      .replaceAll("<", "&lt;")
      .replaceAll(">", "&gt;")
      .replaceAll('"', "&quot;")
      .replaceAll("'", "&#39;");
  }

  function formatBytes(bytes) {
    if (!bytes) return "0 o";
    const units = ["o", "Ko", "Mo", "Go"];
    let value = bytes;
    let index = 0;
    while (value >= 1024 && index < units.length - 1) {
      value /= 1024;
      index += 1;
    }
    return `${value.toFixed(value >= 10 || index === 0 ? 0 : 1)} ${units[index]}`;
  }

  function fileTypeLabel(fileName, mimeType) {
    const extension = String(fileName || "").split(".").pop().toLowerCase();
    const types = {
      pdf: ["PDF", "pdf"],
      doc: ["DOC", "word"], docx: ["DOCX", "word"], odt: ["ODT", "word"],
      xls: ["XLS", "excel"], xlsx: ["XLSX", "excel"], csv: ["CSV", "excel"],
      ppt: ["PPT", "powerpoint"], pptx: ["PPTX", "powerpoint"],
      zip: ["ZIP", "archive"], rar: ["RAR", "archive"], "7z": ["7Z", "archive"],
      tar: ["TAR", "archive"], gz: ["GZ", "archive"],
      txt: ["TXT", "text"], rtf: ["RTF", "text"], md: ["MD", "text"],
      png: ["PNG", "image"], jpg: ["JPG", "image"], jpeg: ["JPG", "image"],
      gif: ["GIF", "image"], webp: ["WEBP", "image"], bmp: ["BMP", "image"],
      mp3: ["MP3", "audio"], wav: ["WAV", "audio"], ogg: ["OGG", "audio"],
      m4a: ["M4A", "audio"], mp4: ["MP4", "video"], mov: ["MOV", "video"],
      mkv: ["MKV", "video"], avi: ["AVI", "video"], webm: ["WEBM", "video"]
    };

    if (types[extension]) return types[extension];
    if (mimeType && mimeType.startsWith("image/")) return ["IMAGE", "image"];
    if (mimeType && mimeType.startsWith("video/")) return ["VIDÉO", "video"];
    if (mimeType && mimeType.startsWith("audio/")) return ["AUDIO", "audio"];
    return [(extension ? extension.slice(0, 5).toUpperCase() : "FICHIER"), "generic"];
  }

  // Pont avec la fenêtre Windows (WebView2). Absent dans un navigateur classique.
  const desktop = (() => {
    const webview = window.chrome && window.chrome.webview;
    if (!webview) return null;

    const pending = new Map();
    let sequence = 0;

    webview.addEventListener("message", (event) => {
      const data = event.data;
      if (!data || typeof data !== "object" || !data.rpcId) return;
      const handlers = pending.get(data.rpcId);
      if (!handlers) return;
      pending.delete(data.rpcId);
      if (data.ok === false) handlers.reject(new Error(data.error || "Opération impossible."));
      else handlers.resolve(data);
    });

    return {
      call(type, payload = {}) {
        return new Promise((resolve, reject) => {
          const rpcId = `rpc-${Date.now()}-${++sequence}`;
          pending.set(rpcId, { resolve, reject });
          webview.postMessage({ type, rpcId, ...payload });
          setTimeout(() => {
            if (!pending.has(rpcId)) return;
            pending.delete(rpcId);
            reject(new Error("Le bureau n'a pas répondu."));
          }, 120000);
        });
      },
      send(type, payload = {}) {
        webview.postMessage({ type, ...payload });
      }
    };
  })();

  // ------------------------------------------------- Téléchargements locaux

  const DOWNLOADS_KEY = "whatsup.downloads.v1";

  function readDownloads() {
    try {
      return JSON.parse(localStorage.getItem(DOWNLOADS_KEY) || "{}") || {};
    } catch {
      return {};
    }
  }

  function downloadRecord(message) {
    if (!message || !message.fileId) return null;
    return readDownloads()[message.fileId] || null;
  }

  function rememberDownload(message, record) {
    const all = readDownloads();
    all[message.fileId] = {
      path: record.path || null,
      folder: record.folder || null,
      fileName: message.fileName || "fichier",
      at: Date.now()
    };
    try {
      localStorage.setItem(DOWNLOADS_KEY, JSON.stringify(all));
    } catch {
      /* stockage local indisponible : le bouton restera « Télécharger » */
    }
  }

  function fileUrl(message, download = false) {
    if (!message || !message.fileId) return "";
    return `/api/files/${encodeURIComponent(message.fileId)}${download ? "?download=1" : ""}`;
  }

  let viewerMessage = null;

  function findMessageById(messageId) {
    return state.messages.find((item) => item.id === messageId)
      || state.search.results.find((item) => item.id === messageId)
      || (viewerMessage && viewerMessage.id === messageId ? viewerMessage : null)
      || null;
  }

  function highlightText(value, query) {
    const text = String(value ?? "");
    const needle = String(query || "");
    if (!needle) return escapeHtml(text);

    const haystack = text.toLocaleLowerCase("fr");
    const lowered = needle.toLocaleLowerCase("fr");
    let html = "";
    let cursor = 0;

    while (cursor <= text.length) {
      const at = haystack.indexOf(lowered, cursor);
      if (at < 0) {
        html += escapeHtml(text.slice(cursor));
        break;
      }
      html += escapeHtml(text.slice(cursor, at));
      html += `<mark>${escapeHtml(text.slice(at, at + needle.length))}</mark>`;
      cursor = at + needle.length;
      if (lowered.length === 0) break;
    }

    return html || escapeHtml(text);
  }

  function formatClock(iso) {
    const date = new Date(iso);
    if (Number.isNaN(date.getTime())) return "";
    return date.toLocaleTimeString("fr-FR", { hour: "2-digit", minute: "2-digit" });
  }

  function formatListTime(iso) {
    const date = new Date(iso);
    if (Number.isNaN(date.getTime())) return "";
    const today = new Date();
    if (date.toDateString() === today.toDateString()) return formatClock(iso);
    return date.toLocaleDateString("fr-FR", { day: "2-digit", month: "2-digit" });
  }

  function formatRelative(iso) {
    const date = new Date(iso);
    if (Number.isNaN(date.getTime())) return "";
    const today = new Date();
    if (date.toDateString() === today.toDateString()) return formatClock(iso);
    const yesterday = new Date();
    yesterday.setDate(today.getDate() - 1);
    if (date.toDateString() === yesterday.toDateString()) return "Hier";
    if ((today - date) / 86400000 < 7) {
      return date.toLocaleDateString("fr-FR", { weekday: "long" });
    }
    return date.toLocaleDateString("fr-FR", { day: "2-digit", month: "2-digit" });
  }

  function formatDay(iso) {
    const date = new Date(iso);
    if (Number.isNaN(date.getTime())) return "";
    const today = new Date();
    const yesterday = new Date();
    yesterday.setDate(today.getDate() - 1);
    if (date.toDateString() === today.toDateString()) return "Aujourd'hui";
    if (date.toDateString() === yesterday.toDateString()) return "Hier";
    return date.toLocaleDateString("fr-FR", { weekday: "long", day: "numeric", month: "long" });
  }

  function formatDuration(ms) {
    const total = Math.max(0, Math.round((ms || 0) / 1000));
    const minutes = Math.floor(total / 60);
    const seconds = total % 60;
    return `${minutes}:${String(seconds).padStart(2, "0")}`;
  }

  function toast(message, variant = "") {
    const node = document.createElement("div");
    node.className = `toast ${variant}`.trim();
    node.textContent = message;
    els.toastWrap.appendChild(node);
    setTimeout(() => node.remove(), 4200);
  }

  function previewOf(message) {
    if (!message) return "";
    if (message.isViewOnce) return "👁 Média en vue unique";

    let text = "";
    if (message.kind === "text") text = message.text || "";
    else if (message.kind === "image") text = "Photo";
    else if (message.kind === "video") text = "Vidéo";
    else if (message.kind === "audio") text = "Message vocal";
    else if (message.kind === "poll") text = "Sondage";
    else if (message.kind === "statuscomment") {
      const data = parseMessageJson(message.text);
      text = `💬 ${data.text || "Commentaire de statut"}`;
    }
    else if (message.kind === "call") {
      const data = parseMessageJson(message.text);
      text = (data.kind === "video" ? "Appel vidéo" : "Appel vocal")
        + (data.result === "missed" ? " manqué" : data.result === "declined" ? " refusé" : "");
    }
    else text = message.fileName || "Fichier";

    if (message.direction === "outgoing") {
      if (message.status === "canceled") return "Envoi annulé";
      if (message.status === "queued") return `En attente : ${text}`;
      if (message.status === "failed") return `Non envoyé : ${text}`;
    }

    return text;
  }

  function peerById(peerId) {
    return state.peers.find((peer) => peer.peerId === peerId) || null;
  }

  // Aucun texte n'est affiché sur les photos : un pictogramme neutre est dessiné
  // par le CSS, la photo de profil vient simplement par-dessus.
  function avatarInner(peerId, version, mine = false) {
    // La discussion « Messages à moi-même » affiche ma propre photo de profil.
    const own = mine || Boolean(state.me && peerId === state.me.peerId);
    const stamp = version || (own ? state.me?.avatarVersion : "");
    if (!stamp) return "";
    const url = own
      ? `/api/me/avatar?v=${encodeURIComponent(stamp)}`
      : `/api/peers/${encodeURIComponent(peerId)}/avatar?v=${encodeURIComponent(stamp)}`;
    return `<img class="avatar-photo" src="${url}" alt="" draggable="false" onerror="this.remove()">`;
  }

  function setAvatar(element, peerId, version, mine = false) {
    if (!element) return;
    element.innerHTML = avatarInner(peerId, version, mine);
  }

  function isTyping(peerId) {
    return (state.typing[peerId] || 0) > Date.now();
  }

  function activePeer() {
    if (!state.activePeerId) return null;
    const conversation = state.conversations.find((item) => item.peerId === state.activePeerId);
    if (conversation) {
      return {
        peerId: conversation.peerId,
        name: conversation.name,
        machineName: conversation.machineName,
        ip: conversation.ip,
        online: conversation.online,
        avatarVersion: conversation.avatarVersion,
        self: Boolean(conversation.self),
        blockChat: conversation.blockChat,
        blockStatus: conversation.blockStatus,
        alias: undefined
      };
    }
    return peerById(state.activePeerId);
  }

  // ------------------------------------------------------------------- Data

  async function loadMe() {
    const me = await api("/api/me");
    state.me = me;
    setAvatar(els.myAvatar, "me", me.avatarVersion, true);
    els.myName.textContent = me.displayName;
    els.sizeHint.textContent = `Taille maximale par fichier : ${me.maxFileSizeMb} Mo`;
    els.welcomeFoot.textContent = `Ce poste : ${me.machineName}`;
    document.title = `vibeChat — ${me.displayName}`;

    const insecure = !window.isSecureContext;
    els.secureHint.classList.toggle("hidden", !insecure);
    if (insecure) {
      els.secureHint.href = me.httpsUrl;
      els.callsSecureNote.textContent =
        "Le microphone nécessite la version sécurisée de l'application : utilisez le bouton affiché sous la zone de saisie.";
    } else {
      els.callsSecureNote.textContent = "Le microphone, les vocaux et les appels sont disponibles.";
    }
  }

  async function loadPeers() {
    try {
      state.peers = await api("/api/peers");
      const online = state.peers.filter((peer) => peer.online && !peer.self).length;
      els.onlineBadge.textContent = `${online} en ligne`;
      els.peerCount.textContent = String(state.peers.filter((peer) => !peer.self).length);
      els.footStatus.textContent = online > 0
        ? `${online} poste(s) joignable(s) sur le réseau`
        : "Recherche de postes sur le réseau…";
      renderPeers();

      if (state.activePeerId) {
        const active = activePeer();
        if (active) {
          const blocked = Boolean(active.blockChat);
          const self = Boolean(active.self);
          els.audioCallBtn.disabled = self || !active.online || blocked;
          els.videoCallBtn.disabled = self || !active.online || blocked;
          updatePeerSubtitle();
          syncComposerState();
        }
      }
    } catch {
      els.footStatus.textContent = "Connexion au serveur local perdue";
    }
  }

  async function loadConversations() {
    try {
      state.conversations = await api("/api/conversations");
      renderConversations();
      if (state.activePeerId) {
        const active = state.conversations.find((item) => item.peerId === state.activePeerId);
        if (active) els.peerTitle.innerHTML = escapeHtml(active.name) + streakTag(active);
      }
    } catch {
      /* silencieux : nouvelle tentative au prochain cycle */
    }
  }

  async function loadBlocks() {
    try {
      state.blocks = await api("/api/blocks");
      renderBlocks();
    } catch {
      /* silencieux */
    }
  }

  // ------------------------------------ Onglets Discussions / Appels / Statuts / Contacts

  function updateMain() {
    const chatOpen = Boolean(state.activePeerId);
    const isChats = state.view === "chats";
    els.chatPanel.classList.toggle("hidden", !(isChats && chatOpen));
    els.welcome.classList.toggle("hidden", !(isChats && !chatOpen));
    els.callsHero.classList.toggle("hidden", state.view !== "calls");
    els.statusesHero.classList.toggle("hidden", state.view !== "statuses");
    els.contactsHero.classList.toggle("hidden", state.view !== "contacts");
  }

  function setView(view) {
    state.view = view;
    document.querySelectorAll("[data-view]").forEach((button) => {
      button.classList.toggle("active", button.dataset.view === view);
    });
    els.chatsView.classList.toggle("hidden", view !== "chats");
    els.callsView.classList.toggle("hidden", view !== "calls");
    els.statusesView.classList.toggle("hidden", view !== "statuses");
    els.contactsView.classList.toggle("hidden", view !== "contacts");
    updateMain();

    if (view === "calls") loadCalls();
    if (view === "contacts") loadPeers();
    if (view === "statuses") loadStatuses();
  }

  async function loadCalls() {
    try {
      state.calls = await api("/api/calls");
      renderCalls();
    } catch {
      /* silencieux : nouvelle tentative au prochain cycle */
    }
  }

  function callArrowIcon(incoming, missed) {
    const path = incoming
      ? '<path d="M18 6 6 18"></path><path d="M6 10v8h8"></path>'
      : '<path d="M6 18 18 6"></path><path d="M10 6h8v8"></path>';
    return `<span class="call-arrow${missed ? " missed" : ""}"><svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">${path}</svg></span>`;
  }

  function renderCalls() {
    if (!els.callsList) return;

    const filter = state.filter.trim().toLowerCase();
    const calls = state.calls.filter((item) => !filter || item.name.toLowerCase().includes(filter));

    els.callsEmpty.classList.toggle("hidden", calls.length > 0);
    if (calls.length === 0) {
      els.callsList.innerHTML = "";
      return;
    }

    els.callsList.innerHTML = calls.map((item) => {
      const incoming = item.direction === "incoming";
      const missed = item.status === "missed" || item.status === "declined";
      const label = missed && incoming ? "Manqué" : incoming ? "Entrant" : "Sortant";
      const kindIcon = item.kind === "video"
        ? '<svg viewBox="0 0 24 24" width="17" height="17" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><rect x="2.5" y="6" width="13" height="12" rx="3"></rect><path d="m15.5 12 6-3.5v7z"></path></svg>'
        : '<svg viewBox="0 0 24 24" width="17" height="17" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M6.6 3.5h2.2l1.6 4-1.9 1.4a12 12 0 0 0 5.8 5.8l1.4-1.9 4 1.6v2.2a2 2 0 0 1-2.2 2A16.5 16.5 0 0 1 4.6 5.7a2 2 0 0 1 2-2.2Z"></path></svg>';

      return `
        <div class="call-row ${item.peerId === state.activePeerId ? "active" : ""}" data-call-peer="${escapeHtml(item.peerId)}" role="button" tabindex="0">
          <span class="avatar small avatar-peer">${avatarInner(item.peerId, item.avatarVersion)}</span>
          <span class="call-body">
            <span class="call-name${missed && incoming ? " missed" : ""}">${escapeHtml(item.name)}</span>
            <span class="call-line">${callArrowIcon(incoming, missed && incoming)}${escapeHtml(label)}</span>
          </span>
          <span class="call-side">
            <span class="call-time">${escapeHtml(formatRelative(item.startedAt))}</span>
            <button class="call-mini" type="button" data-call-start="${escapeHtml(item.peerId)}" data-call-kind="${escapeHtml(item.kind)}" title="Rappeler" aria-label="Rappeler">${kindIcon}</button>
            <button class="call-delete" type="button" data-call-delete="${escapeHtml(item.id)}" title="Supprimer de l'historique" aria-label="Supprimer de l'historique">
              <svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M4 7h16"></path><path d="M9 7V4h6v3"></path><path d="M6 7l1 13h10l1-13"></path></svg>
            </button>
          </span>
        </div>`;
    }).join("");
  }

  async function openConversation(peerId) {
    closeChatSearch(false);
    state.activePeerId = peerId;
    els.app.classList.add("chat-open");

    const peer = peerById(peerId) || state.conversations.find((item) => item.peerId === peerId);
    if (!peer) {
      state.activePeerId = null;
      toast("Ce contact n'est plus disponible.", "error");
      return;
    }

    setView("chats");
    autosize();
    setAvatar(els.peerAvatar, peer.peerId, peer.avatarVersion);
    els.peerTitle.innerHTML = escapeHtml(peer.name) + streakTag(state.conversations.find((item) => item.peerId === peerId));
    updatePeerSubtitle();
    syncComposerState();

    renderPeers();
    renderConversations();
    await loadMessages(peerId);

    const conversation = state.conversations.find((item) => item.peerId === peerId);
    if (conversation && conversation.unread > 0) {
      try {
        await api(`/api/peers/${encodeURIComponent(peerId)}/read`, { method: "POST" });
        conversation.unread = 0;
        renderConversations();
      } catch { /* sans conséquence */ }
    }

    if (!activePeer() || !activePeer().blockChat) els.messageInput.focus();
  }

  async function loadMessages(peerId) {
    if (!peerId) return;
    try {
      const messages = await api(`/api/peers/${encodeURIComponent(peerId)}/messages`);
      if (state.activePeerId !== peerId) return;
      state.messages = messages;
      if (state.search.query) await runChatSearch();
      else renderMessages();
    } catch (error) {
      toast(error.message, "error");
    }
  }

  // --------------------------------------------------------------- Renders

  function renderPeers() {
    const filter = state.filter.trim().toLowerCase();
    const peers = state.peers
      .filter((peer) => !filter
        || peer.name.toLowerCase().includes(filter)
        || (peer.machineName || "").toLowerCase().includes(filter))
      .slice()
      .sort((a, b) => Number(b.self) - Number(a.self) || Number(b.online) - Number(a.online) || a.name.localeCompare(b.name));

    if (peers.length === 0) {
      els.peerList.innerHTML = `<p class="empty-hint">${
        filter
          ? "Aucun poste ne correspond à la recherche."
          : "Aucun poste détecté pour l'instant. Vérifiez que les autres PC sont sur le même réseau et que vibeChat y est lancé."
      }</p>`;
      return;
    }

    els.peerList.innerHTML = peers.map((peer) => {
      const machine = peer.machineName && peer.machineName !== peer.name ? ` · ${escapeHtml(peer.machineName)}` : "";
      const flags = peer.blockChat ? " · bloqué" : "";
      const subtitle = peer.self
        ? "Messages à moi-même · ce poste uniquement"
        : `${escapeHtml(peer.online ? "en ligne" : "hors ligne")}${machine}${flags}`;
      const lock = peer.blockChat || peer.blockStatus
        ? '<span class="block-tag" title="Blocage actif">bloqué</span>'
        : "";
      return `
        <button class="peer-row ${peer.peerId === state.activePeerId ? "active" : ""}" data-peer="${escapeHtml(peer.peerId)}" title="${peer.self ? "Discussion avec moi-même" : `Nom de la machine : ${escapeHtml(peer.machineName || peer.name)}`}">
          <span class="avatar small avatar-peer">${avatarInner(peer.peerId, peer.avatarVersion)}</span>
          <span class="peer-body">
            <span class="peer-name">${escapeHtml(peer.name)}${lock}</span>
            <span class="peer-line">${subtitle}</span>
          </span>
          <span class="peer-side">
            <span class="presence${peer.online ? " online" : ""}"></span>
            <span class="peer-port">${peer.self ? "moi" : ""}</span>
          </span>
        </button>`;
    }).join("");
  }

  // Flamme quotidienne : le compteur monte d'un cran par jour d'échange.
  function streakTag(conversation) {
    const count = conversation ? Number(conversation.streak) || 0 : 0;
    if (count <= 0) return "";
    return `<span class="streak-badge" title="Vous vous êtes écrit ${count} jour(s) d'affilée">🔥 ${count}</span>`;
  }

  function renderConversations() {
    const filter = state.filter.trim().toLowerCase();
    const conversations = state.conversations
      .filter((conversation) => state.filterMode !== "unread" || conversation.unread > 0)
      .filter((conversation) => !filter
        || conversation.name.toLowerCase().includes(filter)
        || (conversation.machineName || "").toLowerCase().includes(filter));

    const unreadTotal = state.conversations.reduce((total, item) => total + (item.unread || 0), 0);
    const unreadConversations = state.conversations.filter((item) => (item.unread || 0) > 0).length;
    updateUnreadBadge(unreadConversations);
    if (els.unreadFilterLabel) {
      els.unreadFilterLabel.textContent = unreadTotal > 0 ? `Non lues ${unreadTotal}` : "Non lues";
    }
    if (els.railUnread) {
      // Comme WhatsApp : le badge compte les conversations comportant de
      // nouveaux messages, pas le total des messages non lus.
      els.railUnread.textContent = String(unreadConversations);
      els.railUnread.classList.toggle("hidden", unreadConversations === 0);
    }

    if (conversations.length === 0) {
      els.conversationList.innerHTML = `<p class="empty-hint">${
        filter ? "Aucune discussion trouvée." : "Aucune discussion pour le moment. Choisissez un poste dans l'onglet Contacts."
      }</p>`;
      return;
    }

    els.conversationList.innerHTML = conversations.map((conversation) => {
      const typing = conversation.online && isTyping(conversation.peerId);
      const preview = previewOf(conversation.lastMessage);
      const prefix = conversation.lastMessage.direction === "outgoing" && !conversation.self ? "Vous : " : "";
      const lock = conversation.blockChat
        ? '<span class="block-tag" title="Contact bloqué">bloqué</span>'
        : "";
      return `
        <button class="conversation-row ${conversation.peerId === state.activePeerId ? "active" : ""}${conversation.unread > 0 ? " unread" : ""}" data-peer="${escapeHtml(conversation.peerId)}">
          <span class="avatar small avatar-peer">${avatarInner(conversation.peerId, conversation.avatarVersion)}</span>
          <span class="conversation-body">
            <span class="conversation-name">${escapeHtml(conversation.name)}${lock}${streakTag(conversation)}</span>
            <span class="conversation-preview${typing ? " typing-preview" : ""}">${typing ? "écrit…" : escapeHtml(prefix + preview)}</span>
          </span>
          <span class="conversation-side">
            <span class="conversation-time">${escapeHtml(formatListTime(conversation.lastMessage.createdAt))}</span>
            ${conversation.unread > 0 ? `<span class="unread-badge">${conversation.unread}</span>` : ""}
          </span>
        </button>`;
    }).join("");
  }

  // Nombre de conversations comportant de nouveaux messages : pastille de la
  // barre des tâches et titre de la fenêtre, comme WhatsApp Desktop.
  function updateUnreadBadge(count) {
    const value = Math.max(0, Number(count) || 0);
    if (value === lastBadgeCount) return;
    lastBadgeCount = value;
    if (desktop) desktop.send("app.badge", { count: value });
    document.title = value > 0 ? `(${value}) vibeChat` : "vibeChat";
  }

  function notifyNewMessage(message, peerId) {
    if (!message || message.direction !== "incoming") return;
    if (message.id) {
      if (notifiedMessages.has(message.id)) return;
      notifiedMessages.add(message.id);
      if (notifiedMessages.size > 300) notifiedMessages.clear();
    }

    // Pas de notification pour la conversation déjà ouverte et au premier plan.
    if (peerId === state.activePeerId && document.hasFocus()) return;

    const peer = peerById(peerId) || state.conversations.find((item) => item.peerId === peerId);
    const title = peer ? peer.name : "Nouveau message";
    const body = (previewOf(message) || "Nouveau message").slice(0, 180);

    if (desktop) {
      desktop.send("app.notify", { title, body, peerId });
    } else {
      toast(`${title} : ${body}`);
    }
  }

  function renderMessages() {
    const searching = state.search.active;
    const messages = searching ? state.search.results : state.messages;
    const highlight = searching ? state.search.query : "";

    if (messages.length === 0) {
      els.messages.innerHTML = `<div class="day-separator">${
        searching ? "Aucun message trouvé" : "Aucun message. Écrivez le premier !"
      }</div>`;
      return;
    }

    let html = "";
    let lastDay = "";

    for (const message of messages) {
      const day = formatDay(message.createdAt);
      if (day !== lastDay) {
        html += `<div class="day-separator">${escapeHtml(day)}</div>`;
        lastDay = day;
      }
      html += bubbleHtml(message, highlight);
    }

    els.messages.innerHTML = html;

    if (searching) {
      const active = state.search.results[state.search.index];
      const node = active ? els.messages.querySelector(`[data-message-id="${active.id}"]`) : null;
      if (node) node.scrollIntoView({ block: "center" });
    } else {
      els.messages.scrollTop = els.messages.scrollHeight;
    }
  }

  function bubbleHtml(message, highlight = "") {
    const outgoing = message.direction === "outgoing";
    const current = state.search.active
      && state.search.results[state.search.index]
      && state.search.results[state.search.index].id === message.id;
    const status = outgoing
      ? `<span class="bubble-status ${
          message.status === "failed" ? "failed" : message.status === "queued" ? "queued" : message.status === "canceled" ? "canceled" : ""
        }"${
          message.status === "queued" ? " title=\"En attente : sera envoyé dès que le destinataire sera connecté\"" : ""
        }>${
          message.status === "failed"
            ? "non envoyé"
            : message.status === "canceled"
              ? "⊘ annulé"
              : message.status === "queued"
                ? "en attente"
                : message.status === "delivered" ? "✓✓" : "✓"
        }</span>`
      : "";

    // Un envoi pas encore distribué peut être annulé.
    const canCancel = outgoing && ["queued", "sending"].includes(message.status);
    const cancel = canCancel
      ? `<button class="cancel-send" type="button" data-cancel="${escapeHtml(message.id)}">Annuler l'envoi</button>`
      : "";

    const forwardedLabel = message.isForwarded
      ? '<div class="forwarded-label"><svg viewBox="0 0 24 24" width="12" height="12" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m9 6 6 6-6 6"></path><path d="M4 12h11"></path></svg>Transféré</div>'
      : "";

    let content = "";
    if (message.isViewOnce) {
      content = viewOnceCardHtml(message);
    } else if (message.kind === "poll") {
      content = pollHtml(message);
    } else if (message.kind === "call") {
      content = callBubbleHtml(message);
    } else if (message.kind === "image" && message.fileId) {
      const url = fileUrl(message);
      content = `<button class="image-button" type="button" data-image-open="${escapeHtml(message.id)}" title="Ouvrir la photo"><img src="${url}" alt="${escapeHtml(message.fileName || "image")}" loading="lazy"></button>`;
    } else if (message.kind === "video" && message.fileId) {
      const url = `/api/files/${encodeURIComponent(message.fileId)}`;
      content = `
        <video class="bubble-video" controls preload="metadata" playsinline src="${url}"></video>
        <div class="media-caption">Vidéo${message.durationMs ? ` · ${escapeHtml(formatDuration(message.durationMs))}` : ""}</div>`;
    } else if (message.kind === "audio" && message.fileId) {
      const url = `/api/files/${encodeURIComponent(message.fileId)}`;
      content = `
        <div class="audio-caption">🎤 Message vocal · ${escapeHtml(formatDuration(message.durationMs))}</div>
        <audio controls preload="none" src="${url}"></audio>`;
    } else if (message.kind === "file" && message.fileId) {
      content = fileCardHtml(message, highlight);
    } else if (message.kind === "statuscomment") {
      content = statusCommentHtml(message);
    } else {
      content = `<div class="bubble-text">${highlightText(message.text || "", highlight)}</div>`;
    }

    return `
      <div class="bubble-row ${outgoing ? "outgoing" : "incoming"}${current ? " search-current" : ""}" data-message-id="${escapeHtml(message.id)}">
        <div class="bubble">
          ${forwardedLabel}
          ${content}
          ${translationHtml(message)}
          <div class="bubble-meta">
            <span>${escapeHtml(formatClock(message.createdAt))}${message.editedAt ? " · modifié" : ""}</span>
            ${status}
          </div>
          ${cancel}
        </div>
        <button class="msg-menu-btn" type="button" data-message-menu="${escapeHtml(message.id)}" title="Options du message" aria-label="Options du message">
          <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2.1" stroke-linecap="round" stroke-linejoin="round"><path d="m6 9 6 6 6-6"></path></svg>
        </button>
      </div>`;
  }

  function viewOnceCardHtml(message) {
    const opened = Boolean(message.viewOnceOpened);
    const icon = message.kind === "image" ? "🖼"
      : message.kind === "video" ? "🎬"
        : message.kind === "audio" ? "🎙"
          : "📄";
    const label = message.kind === "image" ? "Photo"
      : message.kind === "video" ? "Vidéo"
        : message.kind === "audio" ? "Audio"
          : "Fichier";
    return `
      <button class="view-once-card${opened ? " opened" : ""}" type="button"
              data-view-once="${escapeHtml(message.id)}"${opened ? " disabled" : ""}>
        <span class="view-once-icon">${opened ? "✓" : icon}</span>
        <span class="view-once-text">
          <strong>${label} · vue unique</strong>
          <small>${opened ? "Déjà ouverte" : "Appuyer pour voir (une seule fois)"}</small>
        </span>
      </button>`;
  }

  function statusCommentHtml(message) {
    const data = parseMessageJson(message.text);
    const label = message.direction === "outgoing"
      ? "Vous avez commenté un statut"
      : "A commenté votre statut";
    return `
      <div class="status-comment-bubble">
        <div class="status-comment-label">💬 ${label}</div>
        <div class="bubble-text">${escapeHtml(data.text || message.text || "")}</div>
      </div>`;
  }

  function parseMessageJson(text) {
    try { return JSON.parse(text || "{}"); } catch { return {}; }
  }

  function pollHtml(message) {
    const poll = parseMessageJson(message.text);
    const options = Array.isArray(poll.options) ? poll.options : [];
    const votes = Array.isArray(poll.votes) ? poll.votes : [];
    const mine = voteOption(votes, state.me ? state.me.peerId : null);
    const total = votes.length;

    const optionsHtml = options.map((option) => {
      const count = votes.filter((vote) => vote.option === option.id).length;
      const percent = total === 0 ? 0 : Math.round((count / total) * 100);
      return `
        <button class="poll-option${mine === option.id ? " voted" : ""}" type="button" data-poll-vote="${escapeHtml(message.id)}" data-poll-option="${escapeHtml(option.id)}">
          <span class="poll-label">${escapeHtml(option.text)}</span>
          <span class="poll-bar"><i style="width:${percent}%"></i></span>
          <span class="poll-count">${count}</span>
        </button>`;
    }).join("");

    const voters = options.map((option) => {
      const names = votes.filter((vote) => vote.option === option.id).map((vote) => vote.name || vote.peerId);
      return names.length === 0 ? "" : `<div><strong>${escapeHtml(option.text)}</strong> : ${escapeHtml(names.join(", "))}</div>`;
    }).join("");

    return `
      <div class="poll-card">
        <div class="poll-question">${escapeHtml(poll.question || "Sondage")}</div>
        ${optionsHtml}
        <div class="poll-foot">${total} vote${total > 1 ? "s" : ""} · Cliquez pour voter</div>
        ${voters ? `<div class="poll-voters">${voters}</div>` : ""}
      </div>`;
  }

  function voteOption(votes, peerId) {
    if (!peerId) return null;
    const found = votes.find((vote) => vote.peerId === peerId);
    return found ? found.option : null;
  }

  function callBubbleHtml(message) {
    const data = parseMessageJson(message.text);
    const video = data.kind === "video";
    const incoming = message.direction === "incoming";
    const missed = data.result === "missed" || data.result === "declined";
    const title = video ? "Appel vidéo" : "Appel vocal";
    const line = data.result === "answered"
      ? formatDuration(message.durationMs || 0)
      : data.result === "declined" ? "Refusé" : (incoming ? "Manqué" : "Sans réponse");
    const icon = video
      ? '<svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><rect x="2.5" y="6" width="13" height="12" rx="3"></rect><path d="m15.5 12 6-3.5v7z"></path></svg>'
      : '<svg viewBox="0 0 24 24" width="20" height="20" fill="currentColor"><path d="M6.6 3.5h2.2l1.6 4-1.9 1.4a12 12 0 0 0 5.8 5.8l1.4-1.9 4 1.6v2.2a2 2 0 0 1-2.2 2A16.5 16.5 0 0 1 4.6 5.7a2 2 0 0 1 2-2.2Z"></path></svg>';

    return `
      <div class="call-bubble">
        <span class="call-icon">${icon}</span>
        <span>
          <span class="call-title">${escapeHtml(title)}</span><br>
          <span class="call-line${missed && incoming ? " missed" : ""}">${escapeHtml(line)}</span>
        </span>
        ${!incoming ? "" : `<button class="call-retry" type="button" data-call-retry="${escapeHtml(message.peerId)}" data-call-kind="${video ? "video" : "audio"}">Rappeler</button>`}
      </div>`;
  }

  function fileCardHtml(message, highlight = "") {
    const name = message.fileName || "fichier";
    const [label, variant] = fileTypeLabel(name, message.mimeType);
    const record = downloadRecord(message);
    const incoming = message.direction === "incoming";

    // Comme dans WhatsApp : le destinataire voit d'abord « Télécharger »,
    // puis « Ouvrir » et « Enregistrer sous… » une fois le fichier téléchargé.
    const actions = incoming && !record
      ? `<div class="file-actions single">
           <button class="file-action" type="button" data-file-download="${escapeHtml(message.id)}" data-file-save="0">Télécharger</button>
         </div>`
      : `<div class="file-actions">
           <button class="file-action" type="button" data-file-open="${escapeHtml(message.id)}">Ouvrir</button>
           <span class="file-action-sep"></span>
           <button class="file-action" type="button" data-file-download="${escapeHtml(message.id)}" data-file-save="1">Enregistrer sous…</button>
         </div>`;

    return `
      <div class="file-card">
        <div class="file-main">
          <span class="file-type-icon ${variant}">${escapeHtml(label)}</span>
          <span class="file-info">
            <span class="file-name" title="${escapeHtml(name)}">${highlightText(name, highlight)}</span>
            <span class="file-size">${escapeHtml(label)} · ${escapeHtml(formatBytes(message.fileSize))}</span>
            ${record ? `<span class="file-saved" title="${escapeHtml(record.path || "Téléchargé")}">Enregistré sur ce poste</span>` : ""}
          </span>
        </div>
        ${actions}
      </div>`;
  }

  // ------------------------------------------------- Fichiers : ouvrir / sauver

  function refreshAfterDownload(message) {
    renderMessages();
    if (viewerMessage && viewerMessage.id === message.id) updateViewerState();
  }

  async function downloadMessageFile(message, saveAs = false) {
    if (!message || !message.fileId) return;

    const record = downloadRecord(message);
    const chosenSaveAs = Boolean(saveAs || record);
    const url = new URL(fileUrl(message, true), location.href).href;

    if (desktop) {
      try {
        toast(chosenSaveAs ? "Enregistrement du fichier…" : "Téléchargement du fichier…");
        const result = await desktop.call("file.save", {
          url,
          fileName: message.fileName || "fichier",
          saveAs: chosenSaveAs
        });
        if (result.canceled) return;

        rememberDownload(message, { path: result.path, folder: result.folder });
        refreshAfterDownload(message);
        toast(`Fichier enregistré dans ${result.folder}`, "success");
      } catch (error) {
        toast(error.message, "error");
      }
      return;
    }

    // Navigateur : téléchargement classique, état conservé localement.
    const link = document.createElement("a");
    link.href = url;
    link.download = message.fileName || "fichier";
    document.body.appendChild(link);
    link.click();
    link.remove();
    rememberDownload(message, { path: null, folder: null });
    refreshAfterDownload(message);
    toast("Téléchargement lancé.", "success");
  }

  async function openMessageFile(message) {
    if (!message || !message.fileId) return;
    if (message.kind === "image") {
      openImageViewer(message);
      return;
    }

    const record = downloadRecord(message);
    if (desktop) {
      try {
        toast("Ouverture du fichier…");
        await desktop.call("file.open", {
          url: new URL(fileUrl(message), location.href).href,
          fileName: message.fileName || "fichier",
          fileId: message.fileId,
          path: record && record.path ? record.path : ""
        });
      } catch (error) {
        toast(error.message, "error");
      }
      return;
    }

    window.open(fileUrl(message), "_blank", "noopener");
  }

  async function openImageExternally(message) {
    if (!message || !message.fileId) return;
    const record = downloadRecord(message);

    if (desktop) {
      try {
        await desktop.call("file.open", {
          url: new URL(fileUrl(message), location.href).href,
          fileName: message.fileName || "image",
          fileId: message.fileId,
          path: record && record.path ? record.path : ""
        });
      } catch (error) {
        toast(error.message, "error");
      }
      return;
    }

    window.open(fileUrl(message), "_blank", "noopener");
  }

  // ---------------------------------------------------- Visualiseur d'image

  function openImageViewer(message) {
    if (!message || !message.fileId) return;
    viewerMessage = message;

    const peer = activePeer();
    els.viewerImage.src = fileUrl(message);
    els.viewerTitle.textContent = peer ? peer.name : "Photo";
    els.viewerTime.textContent = `${formatDay(message.createdAt)} à ${formatClock(message.createdAt)}`;
    els.viewerDownload.classList.remove("hidden");
    els.imageViewer.classList.remove("hidden");
    updateViewerState();
  }

  function closeImageViewer() {
    viewerMessage = null;
    els.imageViewer.classList.add("hidden");
    els.viewerImage.removeAttribute("src");
  }

  function updateViewerState() {
    if (!viewerMessage) return;
    const record = downloadRecord(viewerMessage);
    els.viewerDownload.title = record ? "Enregistrer sous…" : "Télécharger";
    els.viewerDownload.classList.toggle("saved", Boolean(record));
  }

  // ------------------------------------------------- Recherche dans la conversation

  function openChatSearch() {
    if (!state.activePeerId) return;
    els.chatSearchBar.classList.remove("hidden");
    els.chatSearchBtn.classList.add("active");
    els.chatSearchInput.focus();
    els.chatSearchInput.select();
  }

  function closeChatSearch(render = true) {
    clearTimeout(state.search.timer);
    state.search.query = "";
    state.search.results = [];
    state.search.index = 0;
    state.search.active = false;
    if (els.chatSearchInput) els.chatSearchInput.value = "";
    els.chatSearchBar.classList.add("hidden");
    els.chatSearchBtn.classList.remove("active");
    els.chatSearchCount.textContent = "";
    updateChatSearchUi();
    if (render) renderMessages();
  }

  async function runChatSearch() {
    const query = els.chatSearchInput.value.trim();
    const peerId = state.activePeerId;
    state.search.query = query;

    if (!peerId || query.length === 0) {
      state.search.results = [];
      state.search.index = 0;
      state.search.active = false;
      updateChatSearchUi();
      renderMessages();
      return;
    }

    try {
      const results = await api(`/api/peers/${encodeURIComponent(peerId)}/search?q=${encodeURIComponent(query)}`);
      if (peerId !== state.activePeerId || query !== state.search.query) return;
      state.search.results = Array.isArray(results) ? results : [];
      state.search.index = 0;
      state.search.active = true;
    } catch (error) {
      if (peerId !== state.activePeerId) return;
      state.search.results = [];
      state.search.active = true;
      toast(error.message, "error");
    }

    updateChatSearchUi();
    renderMessages();
  }

  function updateChatSearchUi() {
    const total = state.search.results.length;
    els.chatSearchCount.textContent = !state.search.query
      ? ""
      : total === 0 ? "Aucun résultat" : `${state.search.index + 1} sur ${total}`;
    els.chatSearchPrev.disabled = total < 2;
    els.chatSearchNext.disabled = total < 2;
  }

  function stepSearch(delta) {
    const total = state.search.results.length;
    if (total === 0) return;
    state.search.index = (state.search.index + delta + total) % total;
    updateChatSearchUi();
    renderMessages();
  }

  // -------------------------------------------------- Fenêtre de bureau

  function setupDesktop() {
    if (!desktop) return;

    document.body.classList.add("desktop");
    els.winControls.setAttribute("aria-hidden", "false");

    document.addEventListener("click", (event) => {
      const button = event.target.closest("[data-win]");
      if (!button) return;
      desktop.send(`window:${button.dataset.win}`);
    });

    const titlebar = document.querySelector(".titlebar");
    if (titlebar) {
      titlebar.addEventListener("mousedown", (event) => {
        if (event.button !== 0) return;
        if (event.target.closest("button, a, input")) return;
        desktop.send("window:drag");
      });
    }

    desktop.call("app.info").catch(() => { /* pont indisponible */ });
  }

  // ------------------------------------------------------------------ Envoi

  function syncComposerState() {
    const peer = activePeer();
    const blocked = Boolean(peer && peer.blockChat);
    els.blockedBanner.classList.toggle("hidden", !blocked);
    [els.messageInput, els.sendBtn, els.recordBtn, els.attachBtn].forEach((element) => {
      if (element) element.disabled = blocked;
    });
    if (peer) {
      const self = Boolean(peer.self);
      els.audioCallBtn.disabled = self || !peer.online || blocked;
      els.videoCallBtn.disabled = self || !peer.online || blocked;
    }
  }

  async function sendText() {
    const text = els.messageInput.value.trim();
    if (!text || !state.activePeerId) return;
    if (activePeer()?.blockChat) {
      toast("Vous avez bloqué ce contact : débloquez-le pour discuter.", "error");
      return;
    }

    els.messageInput.value = "";
    autosize();
    stopTyping();
    els.sendBtn.disabled = true;

    try {
      await api(`/api/peers/${encodeURIComponent(state.activePeerId)}/messages`, {
        method: "POST",
        body: JSON.stringify({ text })
      });
      await Promise.all([loadMessages(state.activePeerId), loadConversations()]);
    } catch (error) {
      toast(error.message, "error");
    } finally {
      els.sendBtn.disabled = false;
      els.messageInput.focus();
    }
  }

  function checkSize(file) {
    const maxMb = state.me ? state.me.maxFileSizeMb : 64;
    if (file.size > maxMb * 1024 * 1024) {
      toast(`Fichier trop volumineux : ${maxMb} Mo maximum.`, "error");
      return false;
    }
    return true;
  }

  async function uploadFile(file, kind, durationMs = 0) {
    if (!state.activePeerId || !file) return;
    if (kind === "audio" && !file.name) file = new File([file], `vocal-${Date.now()}.webm`, { type: file.type || "audio/webm" });
    if (!checkSize(file)) return;

    const form = new FormData();
    form.append("file", file, file.name || "fichier");
    form.append("kind", kind);
    form.append("durationMs", String(Math.round(durationMs || 0)));
    if (state.viewOnce && kind !== "file") form.append("viewOnce", "1");

    toast(state.viewOnce && kind !== "file" ? `Envoi en vue unique de « ${file.name} »…` : `Envoi de « ${file.name} »…`);
    try {
      await api(`/api/peers/${encodeURIComponent(state.activePeerId)}/upload`, { method: "POST", body: form });
      await Promise.all([loadMessages(state.activePeerId), loadConversations()]);
    } catch (error) {
      toast(error.message, "error");
      await Promise.all([loadMessages(state.activePeerId), loadConversations()]);
    }
  }

  function kindForFile(file) {
    if (file.type.startsWith("image/")) return "image";
    if (file.type.startsWith("video/")) return "video";
    if (file.type.startsWith("audio/")) return "audio";
    return "file";
  }

  // ------------------------------------------------------------- Vocal

  async function toggleRecording() {
    if (state.recording) {
      stopRecording(true);
      return;
    }

    if (!navigator.mediaDevices || !window.MediaRecorder) {
      toast("Enregistrement vocal non disponible dans ce navigateur. Ajoutez un fichier audio avec le bouton trombone.", "error");
      return;
    }

    let stream;
    try {
      stream = await navigator.mediaDevices.getUserMedia({ audio: true });
    } catch {
      toast("Micro inaccessible. Autorisez le microphone dans la version sécurisée de l'application.", "error");
      return;
    }

    const mimeType = MediaRecorder.isTypeSupported("audio/webm;codecs=opus")
      ? "audio/webm;codecs=opus"
      : (MediaRecorder.isTypeSupported("audio/webm") ? "audio/webm" : "");

    const recorder = mimeType ? new MediaRecorder(stream, { mimeType }) : new MediaRecorder(stream);
    const chunks = [];
    const startedAt = Date.now();

    recorder.ondataavailable = (event) => {
      if (event.data && event.data.size > 0) chunks.push(event.data);
    };

    recorder.onstop = () => {
      stream.getTracks().forEach((track) => track.stop());
      const duration = Date.now() - startedAt;
      clearInterval(state.recording.timer);
      state.recording = null;
      els.recordBar.classList.add("hidden");
      els.recordBtn.classList.remove("recording");
      els.recordTime.textContent = "0:00";

      if (state.cancelRecording) {
        state.cancelRecording = false;
        return;
      }

      if (chunks.length === 0) {
        toast("Enregistrement vide.", "error");
        return;
      }

      const blob = new Blob(chunks, { type: recorder.mimeType || "audio/webm" });
      const extension = blob.type.includes("ogg") ? "ogg" : "webm";
      const file = new File([blob], `vocal-${Date.now()}.${extension}`, { type: blob.type });
      uploadFile(file, "audio", duration);
    };

    recorder.start();
    state.recording = { recorder, startedAt, timer: null };

    els.recordBar.classList.remove("hidden");
    els.recordBtn.classList.add("recording");
    const updateTimer = () => {
      els.recordTime.textContent = formatDuration(Date.now() - startedAt);
    };
    updateTimer();
    state.recording.timer = setInterval(updateTimer, 500);
  }

  function stopRecording(send) {
    if (!state.recording) return;
    state.cancelRecording = !send;
    try {
      state.recording.recorder.stop();
    } catch {
      state.recording = null;
      els.recordBar.classList.add("hidden");
      els.recordBtn.classList.remove("recording");
    }
  }

  // ------------------------------------------------------- Indicateur « écrit »

  function updatePeerSubtitle() {
    const peer = activePeer();
    if (!peer) return;
    if (peer.self) {
      els.peerSubtitle.textContent = "Messages à moi-même · discussion privée sur ce poste";
      els.peerSubtitle.classList.remove("typing");
      return;
    }
    if (peer.blockChat) {
      els.peerSubtitle.textContent = "Contact bloqué";
      els.peerSubtitle.classList.remove("typing");
      return;
    }
    if (peer.online && state.activePeerId && isTyping(state.activePeerId)) {
      els.peerSubtitle.textContent = "écrit…";
      els.peerSubtitle.classList.add("typing");
      return;
    }
    els.peerSubtitle.classList.remove("typing");
    const machineLine = peer.machineName && peer.machineName !== peer.name ? ` · ${peer.machineName}` : "";
    els.peerSubtitle.textContent = `${peer.online ? "en ligne" : "hors ligne"}${machineLine}`;
  }

  function markTyping(peerId, active) {
    if (!peerId) return;
    if (active) state.typing[peerId] = Date.now() + 6000;
    else delete state.typing[peerId];

    renderConversations();
    if (peerId === state.activePeerId) updatePeerSubtitle();
  }

  function pruneTyping() {
    const now = Date.now();
    let changed = false;
    for (const peerId of Object.keys(state.typing)) {
      if (state.typing[peerId] > now) continue;
      delete state.typing[peerId];
      changed = true;
      if (peerId === state.activePeerId) updatePeerSubtitle();
    }
    if (changed) renderConversations();
  }

  let typingPeerId = null;
  let typingSentAt = 0;
  let typingStopTimer = null;

  function notifyTyping() {
    if (!state.activePeerId) return;
    if (activePeer()?.blockChat || activePeer()?.self) return;
    const peerId = state.activePeerId;
    if (typingPeerId && typingPeerId !== peerId) stopTyping();

    if (typingPeerId !== peerId) {
      typingPeerId = peerId;
      typingSentAt = 0;
    }

    const now = Date.now();
    if (now - typingSentAt > 1800) {
      typingSentAt = now;
      api(`/api/peers/${encodeURIComponent(peerId)}/typing`, {
        method: "POST",
        body: JSON.stringify({ typing: true })
      }).catch(() => { /* sans conséquence */ });
    }

    clearTimeout(typingStopTimer);
    typingStopTimer = setTimeout(stopTyping, 2600);
  }

  function stopTyping() {
    clearTimeout(typingStopTimer);
    const peerId = typingPeerId;
    typingPeerId = null;
    typingSentAt = 0;
    if (!peerId) return;
    api(`/api/peers/${encodeURIComponent(peerId)}/typing`, {
      method: "POST",
      body: JSON.stringify({ typing: false })
    }).catch(() => { /* sans conséquence */ });
  }

  // ------------------------------------------------------------- Photo profil

  async function uploadAvatar(file) {
    if (!file || state.avatarUploading) return;
    if (!file.type || !file.type.startsWith("image/")) {
      toast("Choisissez une image (PNG, JPEG, WebP ou GIF).", "error");
      return;
    }
    if (file.size > 4 * 1024 * 1024) {
      toast("Photo trop volumineuse : 4 Mo maximum.", "error");
      return;
    }

    const form = new FormData();
    form.append("file", file, file.name || "avatar.png");
    state.avatarUploading = true;
    try {
      await api("/api/me/avatar", { method: "PUT", body: form });
      await loadMe();
      toast("Photo de profil mise à jour : elle est visible par les postes du réseau.", "success");
    } catch (error) {
      toast(error.message, "error");
    } finally {
      state.avatarUploading = false;
    }
  }

  // ----------------------------------------------------------------- Statuts

  async function loadStatuses() {
    try {
      const feed = await api("/api/statuses");
      statusState.mine = feed.mine || [];
      statusState.peers = feed.peers || [];
      renderStatuses();
    } catch {
      /* silencieux */
    }
  }

  function renderStatuses() {
    const mine = statusState.mine;
    const total = statusState.peers.reduce((count, group) => count + group.statuses.filter((item) => !item.viewed).length, 0);

    if (els.railStatus) {
      els.railStatus.textContent = String(total);
      els.railStatus.classList.toggle("hidden", total === 0);
    }
    if (els.statusCount) els.statusCount.textContent = String(statusState.peers.length);

    if (mine.length === 0) {
      els.myStatusLine.textContent = "Ajouter un statut (texte, photo ou vidéo)";
      els.myStatusAvatar.classList.remove("status-unviewed");
      els.myStatusAdd.classList.remove("hidden");
    } else {
      const latest = mine[mine.length - 1];
      const views = mine.reduce((count, item) => count + (item.viewCount || 0), 0);
      els.myStatusLine.textContent = `${mine.length} statut${mine.length > 1 ? "s" : ""} · ${formatRelative(latest.createdAt)} · ${views} vue${views > 1 ? "s" : ""}`;
      els.myStatusAvatar.classList.add("status-unviewed");
      els.myStatusAdd.classList.add("hidden");
    }

    const recent = statusState.peers.filter((group) => group.statuses.some((item) => !item.viewed));
    const viewed = statusState.peers.filter((group) => group.statuses.every((item) => item.viewed));

    els.statusList.innerHTML = recent.map((group) => statusRowHtml(group, true)).join("");
    els.statusEmpty.classList.toggle("hidden", statusState.peers.length > 0);
    els.statusViewedWrap.classList.toggle("hidden", viewed.length === 0);
    els.statusViewedList.innerHTML = viewed.map((group) => statusRowHtml(group, false)).join("");
  }

  function statusRowHtml(group, unviewed) {
    const latest = group.statuses[group.statuses.length - 1];
    const count = group.statuses.length;
    const label = unviewed
      ? `${count} statut${count > 1 ? "s" : ""} · ${formatRelative(latest.createdAt)}`
      : `${count} statut${count > 1 ? "s" : ""} · vu${count > 1 ? "s" : ""} ${formatRelative(latest.createdAt)}`;

    return `
      <button class="status-row" data-status-peer="${escapeHtml(group.peerId)}" type="button">
        <span class="avatar status-avatar ${unviewed ? "status-unviewed" : ""}">${avatarInner(group.peerId, group.avatarVersion)}</span>
        <span class="status-body">
          <span class="status-name">${escapeHtml(group.name)}</span>
          <span class="status-line">${escapeHtml(label)}</span>
        </span>
      </button>`;
  }

  function openMyStatuses() {
    if (statusState.mine.length === 0) {
      openStatusComposer();
      return;
    }
    openStatusViewer({
      owner: {
        peerId: null,
        name: state.me?.displayName || "Mon statut",
        avatarVersion: state.me?.avatarVersion
      },
      items: statusState.mine.slice(),
      mine: true
    });
  }

  function openPeerStatuses(peerId) {
    const group = statusState.peers.find((item) => item.peerId === peerId);
    if (!group) return;
    openStatusViewer({
      owner: {
        peerId: group.peerId,
        name: group.name,
        avatarVersion: group.avatarVersion
      },
      items: group.statuses.slice(),
      mine: false
    });
  }

  function openStatusViewer({ owner, items, mine }) {
    if (!items || items.length === 0) return;
    statusState.viewer = { owner, items, index: 0, mine, timer: null };
    els.statusViewer.classList.remove("hidden");
    els.statusViewersPanel.classList.add("hidden");
    renderStatusViewer();
  }

  function statusMediaUrl(item) {
    if (statusState.viewer.mine) {
      return `/api/statuses/${encodeURIComponent(item.id)}/media`;
    }
    return `/api/statuses/peer/${encodeURIComponent(statusState.viewer.owner.peerId)}/${encodeURIComponent(item.id)}/media`;
  }

  function renderStatusViewer() {
    const viewer = statusState.viewer;
    const item = viewer.items[viewer.index];
    if (!item) {
      closeStatusViewer();
      return;
    }

    clearInterval(viewer.timer);
    viewer.timer = null;

    els.statusProgress.innerHTML = viewer.items.map((_, index) => {
      const width = index < viewer.index ? 100 : 0;
      const active = index === viewer.index ? " active" : "";
      return `<span class="status-seg${active}"><i style="width:${width}%"></i></span>`;
    }).join("");

    setAvatar(
      els.statusViewerAvatar,
      viewer.owner.peerId || "me",
      viewer.owner.avatarVersion,
      viewer.mine
    );
    els.statusViewerAvatar.classList.toggle("status-unviewed", !viewer.mine);
    els.statusViewerName.textContent = viewer.mine ? "Mon statut" : viewer.owner.name;
    els.statusViewerTime.textContent = formatRelative(item.createdAt);
    els.statusViewerCaption.textContent = item.caption || "";
    renderStatusActivity(item);
    renderStatusPrivacyEditor(item);

    // Statut personnel : ni j'aime ni commentaire, seulement les vues ;
    // le bouton ⏸ du haut (et la touche Espace) met le partage en pause.
    const paused = Boolean(viewer.mine && item.paused);
    els.statusViewer.classList.toggle("mine", viewer.mine);
    els.statusViewer.classList.toggle("paused", paused);

    if (els.statusPauseTop) {
      els.statusPauseTop.classList.toggle("hidden", !viewer.mine);
      els.statusPauseTop.classList.toggle("paused", Boolean(item.paused));
      els.statusPauseTop.title = item.paused
        ? "Reprendre le partage (Espace)"
        : "Mettre en pause (Espace)";
      els.statusPauseTop.setAttribute("aria-label", els.statusPauseTop.title);
    }
    if (els.statusReactRow) els.statusReactRow.classList.toggle("hidden", viewer.mine);
    if (els.statusOwnNote) els.statusOwnNote.classList.toggle("hidden", !viewer.mine);
    if (els.statusOwnViews) els.statusOwnViews.textContent = String(item.viewCount || 0);

    if (viewer.mine) {
      els.statusViewsBtn.classList.remove("hidden");
      els.statusViewerViews.textContent = String(item.viewCount || 0);
      renderStatusViewers(item);
    } else {
      els.statusViewsBtn.classList.add("hidden");
      els.statusViewersPanel.classList.add("hidden");
    }

    if (item.kind === "text") {
      const background = item.background || "#0A7C5A";
      els.statusContent.innerHTML = `<div class="status-text-card" style="background:${escapeHtml(background)}"><p>${escapeHtml(item.text || "")}</p></div>`;
      if (!paused) startStatusProgress(5000);
    } else if (item.kind === "image") {
      els.statusContent.innerHTML = `<img class="status-media" src="${statusMediaUrl(item)}" alt="Statut photo">`;
      if (!paused) startStatusProgress(6000);
    } else {
      els.statusContent.innerHTML = `<video class="status-media" src="${statusMediaUrl(item)}" autoplay playsinline controls></video>`;
      const video = els.statusContent.querySelector("video");
      if (video) {
        if (paused) video.pause();
        video.addEventListener("ended", nextStatus);
        video.addEventListener("timeupdate", () => {
          if (!video.duration || Number.isNaN(video.duration)) return;
          setActiveProgress((video.currentTime / video.duration) * 100);
        });
      }
    }

    els.statusPrev.classList.toggle("hidden", viewer.index === 0);
    els.statusNext.classList.toggle("hidden", viewer.index === viewer.items.length - 1);

    if (!viewer.mine && !item.viewed) {
      item.viewed = true;
      const group = statusState.peers.find((entry) => entry.peerId === viewer.owner.peerId);
      const stored = group && group.statuses.find((entry) => entry.id === item.id);
      if (stored) stored.viewed = true;
      api(`/api/statuses/peer/${encodeURIComponent(viewer.owner.peerId)}/${encodeURIComponent(item.id)}/view`, { method: "POST" })
        .then(() => loadStatuses())
        .catch(() => { /* la vue sera signalée à la prochaine consultation */ });
    }
  }

  function setActiveProgress(percent) {
    const bar = els.statusProgress.querySelector(`.status-seg.active i`);
    if (bar) bar.style.width = `${Math.min(100, Math.max(0, percent))}%`;
  }

  function startStatusProgress(duration) {
    const viewer = statusState.viewer;
    clearInterval(viewer.timer);
    const startedAt = performance.now();
    viewer.timer = setInterval(() => {
      const ratio = Math.min(1, (performance.now() - startedAt) / duration);
      setActiveProgress(ratio * 100);
      if (ratio >= 1) {
        clearInterval(viewer.timer);
        nextStatus();
      }
    }, 60);
  }

  function nextStatus() {
    const viewer = statusState.viewer;
    clearInterval(viewer.timer);
    if (viewer.index >= viewer.items.length - 1) {
      closeStatusViewer();
      return;
    }
    viewer.index += 1;
    renderStatusViewer();
  }

  function previousStatus() {
    const viewer = statusState.viewer;
    if (viewer.index <= 0) return;
    clearInterval(viewer.timer);
    viewer.index -= 1;
    renderStatusViewer();
  }

  function closeStatusViewer() {
    clearInterval(statusState.viewer.timer);
    const video = els.statusContent.querySelector("video");
    if (video) {
      try { video.pause(); } catch { /* ignoré */ }
    }
    els.statusViewer.classList.add("hidden");
    els.statusViewersPanel.classList.add("hidden");
    els.statusContent.innerHTML = "";
    loadStatuses();
  }

  function renderStatusViewers(item) {
    const views = item.views || [];
    els.statusViewersList.innerHTML = views.length === 0
      ? '<p class="empty-hint">Personne n\'a encore vu ce statut.</p>'
      : views.map((view) => `
        <div class="status-viewer-row">
          <span class="avatar tiny">${avatarInner(view.peerId, null)}</span>
          <span class="viewer-heart" title="A vu votre statut">💚</span>
          <span class="status-viewer-row-name">${escapeHtml(view.name || view.peerId)}</span>
          <span class="status-viewer-row-time">${escapeHtml(formatClock(view.at))}</span>
        </div>`).join("");
  }

  // -------------------------------------------------- Création d'un statut

  function openStatusComposer() {
    statusState.tab = "text";
    statusState.background = "#0A7C5A";
    statusState.file = null;
    statusState.durationMs = 0;
    if (statusState.previewUrl) {
      URL.revokeObjectURL(statusState.previewUrl);
      statusState.previewUrl = null;
    }

    els.statusTextInput.value = "";
    els.statusCaption.value = "";
    els.statusImagePreview.classList.add("hidden");
    els.statusImagePreview.removeAttribute("src");
    els.statusVideoPreview.classList.add("hidden");
    els.statusVideoPreview.removeAttribute("src");
    els.statusMediaPreview.classList.add("hidden");
    els.statusTextPreview.classList.remove("hidden");
    els.statusTextPreview.style.background = statusState.background;
    els.statusFileInput.value = "";
    statusState.privacyPeers.clear();
    els.statusPrivacy.value = "all";
    renderPrivacyPeers();
    renderStatusColors();
    setStatusTab("text");
    els.statusComposer.classList.remove("hidden");
    els.statusTextInput.focus();
  }

  function closeStatusComposer() {
    els.statusComposer.classList.add("hidden");
    if (statusState.previewUrl) {
      URL.revokeObjectURL(statusState.previewUrl);
      statusState.previewUrl = null;
    }
  }

  function renderStatusColors() {
    els.statusColors.innerHTML = STATUS_COLORS.map((color) => `
      <button type="button" class="status-color${color === statusState.background ? " active" : ""}"
              data-status-color="${color}" style="background:${color}" aria-label="Fond ${color}"></button>`).join("");
  }

  function setStatusTab(tab) {
    statusState.tab = tab;
    els.statusTabs.querySelectorAll("[data-status-tab]").forEach((button) => {
      button.classList.toggle("active", button.dataset.statusTab === tab);
    });

    const isText = tab === "text";
    els.statusTextPreview.classList.toggle("hidden", !isText);
    els.statusMediaPreview.classList.toggle("hidden", isText);
    els.statusFileInput.accept = tab === "video" ? "video/*" : "image/*";

    if (!isText && !statusState.file) {
      els.statusFileInput.click();
    }
  }

  function setStatusFile(file) {
    if (!file) return;
    const kind = file.type.startsWith("video/") ? "video" : file.type.startsWith("image/") ? "image" : "";
    if (kind.length === 0 || kind !== statusState.tab) {
      toast(`Choisissez ${statusState.tab === "video" ? "une vidéo" : "une photo"}.`, "error");
      return;
    }
    if (!checkSize(file)) return;

    statusState.file = file;
    statusState.durationMs = 0;
    if (statusState.previewUrl) URL.revokeObjectURL(statusState.previewUrl);
    statusState.previewUrl = URL.createObjectURL(file);

    if (kind === "image") {
      els.statusImagePreview.src = statusState.previewUrl;
      els.statusImagePreview.classList.remove("hidden");
      els.statusVideoPreview.classList.add("hidden");
    } else {
      els.statusVideoPreview.src = statusState.previewUrl;
      els.statusVideoPreview.classList.remove("hidden");
      els.statusImagePreview.classList.add("hidden");
      els.statusVideoPreview.onloadedmetadata = () => {
        statusState.durationMs = Math.round((els.statusVideoPreview.duration || 0) * 1000);
      };
    }
    els.statusMediaPreview.classList.remove("hidden");
  }

  async function publishStatus() {
    const tab = statusState.tab;
    els.statusPublish.disabled = true;

    try {
      if (tab === "text") {
        const text = els.statusTextInput.value.trim();
        if (!text) {
          toast("Tapez un texte pour votre statut.", "error");
          return;
        }
        await api("/api/statuses", {
          method: "POST",
          body: JSON.stringify({
            text,
            background: statusState.background,
            caption: els.statusCaption.value.trim(),
            privacy: els.statusPrivacy.value,
            privacyList: Array.from(statusState.privacyPeers)
          })
        });
      } else {
        if (!statusState.file) {
          toast("Choisissez un fichier pour votre statut.", "error");
          return;
        }
        const form = new FormData();
        form.append("file", statusState.file, statusState.file.name || "statut");
        form.append("caption", els.statusCaption.value.trim());
        form.append("durationMs", String(statusState.durationMs || 0));
        form.append("privacy", els.statusPrivacy.value);
        form.append("privacyList", JSON.stringify(Array.from(statusState.privacyPeers)));
        await api("/api/statuses/upload", { method: "POST", body: form });
      }

      closeStatusComposer();
      await loadStatuses();
      toast("Statut publié pour les postes du réseau.", "success");
      setView("statuses");
    } catch (error) {
      toast(error.message, "error");
    } finally {
      els.statusPublish.disabled = false;
    }
  }

  // ------------------------------------------------------------------ Blocage

  function renderBlocks() {
    if (!els.blockedList) return;
    els.blockedWrap.classList.toggle("hidden", state.blocks.length === 0);
    els.blockedCount.textContent = String(state.blocks.length);

    els.blockedList.innerHTML = state.blocks.map((block) => {
      const tags = [];
      if (block.chat) tags.push("discussion bloquée");
      if (block.status) tags.push("statuts masqués");
      return `
        <div class="blocked-row">
          <span class="avatar small">${avatarInner(block.peerId, block.avatarVersion)}</span>
          <span class="blocked-body">
            <span class="blocked-name">${escapeHtml(block.name)}</span>
            <span class="blocked-line">${escapeHtml(tags.join(" · "))}</span>
          </span>
          <span class="blocked-actions">
            ${block.chat ? `<button class="text-btn" type="button" data-unblock-chat="${escapeHtml(block.peerId)}">Débloquer</button>` : ""}
            ${block.status ? `<button class="text-btn" type="button" data-unhide-status="${escapeHtml(block.peerId)}">Afficher mes statuts</button>` : ""}
          </span>
        </div>`;
    }).join("");
  }

  function openContactMenu() {
    const peer = activePeer();
    if (!peer) return;
    els.menuName.textContent = peer.name;
    setAvatar(els.menuAvatar, peer.peerId, peer.avatarVersion);
    els.switchChat.classList.toggle("on", Boolean(peer.blockChat));
    els.switchStatus.classList.toggle("on", Boolean(peer.blockStatus));
    // Le renommage et le blocage n'ont pas de sens dans la discussion avec soi-même.
    const self = Boolean(peer.self);
    [els.menuRename, els.menuBlockChat, els.menuBlockStatus].forEach((row) => {
      if (row) row.classList.toggle("hidden", self);
    });
    els.contactMenu.classList.remove("hidden");
  }

  function closeContactMenu() {
    els.contactMenu.classList.add("hidden");
  }

  async function setBlock(peerId, patch) {
    if (!peerId) return;
    try {
      await api(`/api/peers/${encodeURIComponent(peerId)}/block`, {
        method: "PUT",
        body: JSON.stringify(patch)
      });
      await Promise.all([loadPeers(), loadConversations(), loadBlocks()]);
      const peer = activePeer();
      if (peer) {
        els.switchChat.classList.toggle("on", Boolean(peer.blockChat));
        els.switchStatus.classList.toggle("on", Boolean(peer.blockStatus));
        updatePeerSubtitle();
        syncComposerState();
      }
      toast("Réglage de blocage enregistré.", "success");
    } catch (error) {
      toast(error.message, "error");
    }
  }

  async function toggleBlock(kind) {
    const peer = activePeer();
    if (!peer) return;
    if (kind === "chat") await setBlock(peer.peerId, { chat: !peer.blockChat });
    else await setBlock(peer.peerId, { status: !peer.blockStatus });
  }

  // ------------------------------------------------------------- Appels WebRTC

  // Candidats ICE reçus avant l'offre (l'ordre des messages n'est pas garanti).
  const earlyIce = new Map();

  const call = {
    pc: null,
    localStream: null,
    callId: null,
    peerId: null,
    kind: "audio",
    direction: "outgoing",
    connected: false,
    declined: false,
    incoming: false,
    remoteSet: false,
    pendingCandidates: [],
    pendingOffer: null,
    timer: null,
    ringTimer: null,
    startedAt: 0,
    muted: false,
    cameraOff: false
  };

  // Sonnerie et tonalité de retour, générées comme dans WhatsApp (aucun fichier externe).
  const ringtone = { ctx: null, timer: null, mode: null };

  function playRingBurst(ctx, mode) {
    const now = ctx.currentTime;
    const notes = mode === "incoming"
      ? [[0, 880], [0.18, 1174.66], [0.36, 1046.5], [0.54, 1318.51]]
      : [[0, 425], [1.1, 425]];

    for (const [offset, frequency] of notes) {
      const oscillator = ctx.createOscillator();
      const gain = ctx.createGain();
      oscillator.type = "sine";
      oscillator.frequency.value = frequency;
      const start = now + offset;
      const duration = mode === "incoming" ? 0.16 : 0.9;
      gain.gain.setValueAtTime(0.0001, start);
      gain.gain.exponentialRampToValueAtTime(mode === "incoming" ? 0.1 : 0.05, start + 0.04);
      gain.gain.exponentialRampToValueAtTime(0.0001, start + duration);
      oscillator.connect(gain);
      gain.connect(ctx.destination);
      oscillator.start(start);
      oscillator.stop(start + duration + 0.05);
    }
  }

  function startRingtone(mode) {
    if (ringtone.mode === mode) return;
    stopRingtone();

    const AudioCtx = window.AudioContext || window.webkitAudioContext;
    if (!AudioCtx) return;

    try {
      const ctx = new AudioCtx();
      ringtone.ctx = ctx;
      ringtone.mode = mode;
      ctx.resume?.();
      playRingBurst(ctx, mode);
      ringtone.timer = setInterval(() => {
        if (!ringtone.ctx) return;
        if (ringtone.ctx.state === "suspended") ringtone.ctx.resume?.();
        playRingBurst(ringtone.ctx, mode);
      }, 3000);
    } catch {
      ringtone.ctx = null;
      ringtone.mode = null;
    }
  }

  function stopRingtone() {
    clearInterval(ringtone.timer);
    ringtone.timer = null;
    if (ringtone.ctx) {
      try { ringtone.ctx.close(); } catch { /* ignoré */ }
    }
    ringtone.ctx = null;
    ringtone.mode = null;
  }

  function sendSignalRaw(peerId, callId, kind, action, extra = {}) {
    return api(`/api/peers/${encodeURIComponent(peerId)}/call`, {
      method: "POST",
      body: JSON.stringify({
        callId,
        kind,
        action,
        sdp: extra.sdp || null,
        candidate: extra.candidate || null
      })
    }).catch(() => ({ ok: false }));
  }

  function sendCallSignal(action, extra = {}) {
    if (!call.peerId || !call.callId) return Promise.resolve({ ok: false });
    return sendSignalRaw(call.peerId, call.callId, call.kind, action, extra);
  }

  function setCallStatus(text, live = false) {
    els.callStatus.textContent = text;
    els.callStatus.classList.toggle("live", live);
  }

  function formatCallTime(ms) {
    const total = Math.max(0, Math.floor(ms / 1000));
    return `${Math.floor(total / 60)}:${String(total % 60).padStart(2, "0")}`;
  }

  function startCallTimer() {
    clearTimeout(call.ringTimer);
    call.ringTimer = null;
    if (call.timer) return;
    stopRingtone();
    call.connected = true;
    call.startedAt = Date.now();
    call.timer = setInterval(() => {
      setCallStatus(`En communication · ${formatCallTime(Date.now() - call.startedAt)}`, true);
    }, 1000);
  }

  // Au bout de 45 s sans réponse, l'appel est classé manqué des deux côtés.
  function startRingTimeout() {
    clearTimeout(call.ringTimer);
    call.ringTimer = setTimeout(() => {
      if (!call.callId || call.connected) return;
      endCall(true, call.incoming ? "Appel manqué." : "Pas de réponse.");
    }, 45000);
  }

  function showCallOverlay({ name, peerId, status, incoming, kind }) {
    const known = state.peers.find((item) => item.peerId === peerId)
      || state.conversations.find((item) => item.peerId === peerId);
    setAvatar(els.callAvatar, peerId, known ? known.avatarVersion : null);
    els.callName.textContent = name || "Contact";
    setCallStatus(status);
    els.callLayer.classList.remove("hidden");
    els.callIdentity.classList.remove("hidden");
    els.callRemoteVideo.classList.add("hidden");
    els.callRemoteVideo.classList.remove("audio-only");
    els.callLocalVideo.classList.add("hidden");
    els.callAcceptBtn.classList.toggle("hidden", !incoming);
    els.callMuteBtn.classList.toggle("hidden", incoming);
    els.callCamBtn.classList.toggle("hidden", incoming || kind !== "video");
    els.callMuteBtn.classList.remove("active");
    els.callCamBtn.classList.remove("active");
  }

  function attachLocalPreview() {
    if (!call.localStream) return;
    els.callLocalVideo.srcObject = call.localStream;
    if (call.kind === "video") els.callLocalVideo.classList.remove("hidden");
  }

  function preparePeerConnection() {
    const pc = new RTCPeerConnection({ iceServers: [] });
    call.pc = pc;
    call.remoteSet = false;

    if (call.localStream) {
      call.localStream.getTracks().forEach((track) => pc.addTrack(track, call.localStream));
    }

    pc.ontrack = (event) => {
      const stream = event.streams && event.streams[0];
      if (!stream) return;
      els.callRemoteVideo.srcObject = stream;
      els.callRemoteVideo.classList.remove("hidden");
      if (call.kind === "audio") {
        els.callIdentity.classList.remove("hidden");
        els.callRemoteVideo.classList.add("audio-only");
      } else {
        els.callIdentity.classList.add("hidden");
      }
      startCallTimer();
    };

    pc.onicecandidate = (event) => {
      if (event.candidate) sendCallSignal("ice", { candidate: JSON.stringify(event.candidate) });
    };

    pc.onconnectionstatechange = () => {
      if (!call.pc) return;
      if (pc.connectionState === "connected") startCallTimer();
      if (pc.connectionState === "failed") endCall(true, "Appel interrompu.");
    };

    return pc;
  }

  async function getCallMedia(kind) {
    return navigator.mediaDevices.getUserMedia({
      audio: true,
      video: kind === "video" ? { width: { ideal: 1280 }, height: { ideal: 720 } } : false
    });
  }

  function mediaErrorMessage() {
          const secure = " Utilisez la version sécurisée de l'application.";
    return `Micro/caméra inaccessible.${secure}`;
  }

  async function startCall(kind) {
    const peer = activePeer();
    if (!peer || !state.activePeerId) return;

    if (peer.blockChat) {
      toast("Vous avez bloqué ce contact : débloquez-le pour l'appeler.", "error");
      return;
    }
    if (!peer.online) {
      toast("Ce contact est hors ligne : impossible de lancer un appel.", "error");
      return;
    }
    if (call.pc || call.pendingOffer) {
      toast("Un appel est déjà en cours.");
      return;
    }

    if (!navigator.mediaDevices || !window.RTCPeerConnection) {
      toast("Les appels ne sont pas disponibles dans ce navigateur.", "error");
      return;
    }

    let stream;
    try {
      stream = await getCallMedia(kind);
    } catch {
      toast(mediaErrorMessage(), "error");
      return;
    }

    call.localStream = stream;
    call.kind = kind;
    call.peerId = peer.peerId;
    call.incoming = false;
    call.direction = "outgoing";
    call.connected = false;
    call.declined = false;
    call.callId = (crypto.randomUUID && crypto.randomUUID()) || `${Date.now()}-${Math.random().toString(16).slice(2)}`;
    call.pendingCandidates = [];
    earlyIce.delete(call.callId);

    showCallOverlay({
      name: peer.name,
      peerId: peer.peerId,
      status: kind === "video" ? "Appel vidéo en cours…" : "Appel en cours…",
      incoming: false,
      kind
    });
    attachLocalPreview();
    startRingtone("outgoing");
    startRingTimeout();

    const pc = preparePeerConnection();
    try {
      const offer = await pc.createOffer();
      await pc.setLocalDescription(offer);
      const result = await sendCallSignal("offer", { sdp: offer.sdp });
      if (result && result.ok === false) {
        toast("Impossible de joindre ce poste pour l'appel.", "error");
        endCall(false);
      }
    } catch {
      toast("L'appel n'a pas pu démarrer.", "error");
      endCall(false);
    }
  }

  async function handleCallSignal(payload) {
    const action = payload.action;

    if (action === "offer") {
      if (call.pc || call.pendingOffer) {
        sendSignalRaw(payload.peerId, payload.callId, payload.kind || "audio", "busy");
        return;
      }
      call.incoming = true;
      call.peerId = payload.peerId;
      call.callId = payload.callId;
      call.kind = payload.kind === "video" ? "video" : "audio";
      call.direction = "incoming";
      call.connected = false;
      call.declined = false;
      call.pendingOffer = payload.sdp;
      // Les candidats ICE arrivés avant l'offre sont conservés puis rejoués.
      call.pendingCandidates = (earlyIce.get(payload.callId) || []).slice();
      earlyIce.delete(payload.callId);
      const known = peerById(payload.peerId);
      showCallOverlay({
        name: payload.peerName || (known && known.name) || "Contact",
        peerId: payload.peerId,
        status: call.kind === "video" ? "Appel vidéo entrant…" : "Appel entrant…",
        incoming: true,
        kind: call.kind
      });
      startRingtone("incoming");
      startRingTimeout();
      return;
    }

    if (action === "ice") {
      if (!payload.candidate) return;
      let candidate;
      try { candidate = JSON.parse(payload.candidate); } catch { return; }

      if (call.callId && payload.callId === call.callId) {
        if (call.pc && call.remoteSet) {
          try { await call.pc.addIceCandidate(candidate); } catch { /* candidat tardif */ }
        } else {
          call.pendingCandidates.push(candidate);
        }
      } else if (payload.callId) {
        const queued = earlyIce.get(payload.callId) || [];
        queued.push(candidate);
        earlyIce.set(payload.callId, queued);
      }
      return;
    }

    if (!call.callId || payload.callId !== call.callId) return;

    if (action === "answer") {
      if (!call.pc) return;
      try {
        await call.pc.setRemoteDescription({ type: "answer", sdp: payload.sdp });
        call.remoteSet = true;
        await flushCandidates();
        startCallTimer();
      } catch {
        toast("Réponse d'appel invalide.", "error");
        endCall(true);
      }
      return;
    }

    if (action === "end") {
      endCall(false, "Appel terminé.");
      return;
    }

    if (action === "decline") {
      call.declined = true;
      endCall(false, "Appel refusé.");
      return;
    }

    if (action === "busy") {
      endCall(false, "Ce contact est déjà en appel.");
    }
  }

  async function flushCandidates() {
    if (!call.pc) return;
    const candidates = call.pendingCandidates.splice(0);
    for (const candidate of candidates) {
      try { await call.pc.addIceCandidate(candidate); } catch { /* ignoré */ }
    }
  }

  async function acceptCall() {
    if (!call.pendingOffer) return;
    const offer = call.pendingOffer;
    const kind = call.kind;

    let stream;
    try {
      stream = await getCallMedia(kind);
    } catch {
      toast("Micro/caméra inaccessible : impossible de décrocher. Utilisez la version sécurisée de l'application.", "error");
      sendCallSignal("decline");
      endCall(false);
      return;
    }

    stopRingtone();
    clearTimeout(call.ringTimer);
    call.ringTimer = null;
    call.localStream = stream;
    call.incoming = false;
    call.pendingOffer = null;

    els.callAcceptBtn.classList.add("hidden");
    els.callMuteBtn.classList.remove("hidden");
    els.callCamBtn.classList.toggle("hidden", kind !== "video");
    setCallStatus("Connexion…");
    attachLocalPreview();

    try {
      const pc = preparePeerConnection();
      await pc.setRemoteDescription({ type: "offer", sdp: offer });
      call.remoteSet = true;
      await flushCandidates();
      const answer = await pc.createAnswer();
      await pc.setLocalDescription(answer);
      await sendCallSignal("answer", { sdp: answer.sdp });
    } catch {
      toast("Impossible de répondre à l'appel.", "error");
      endCall(true);
    }
  }

  function declineCall() {
    call.declined = true;
    sendCallSignal("decline");
    endCall(false, "Appel refusé.");
  }

  function endCall(notify = true, message = "") {
    if (notify) sendCallSignal("end");
    cleanupCall();
    if (message) toast(message);
  }

  function logCallSummary() {
    if (!call.peerId || !call.callId) return;

    const duration = call.connected ? Math.max(0, Date.now() - call.startedAt) : 0;
    const status = call.connected ? "answered" : call.declined ? "declined" : "missed";
    const payload = {
      peerId: call.peerId,
      kind: call.kind,
      direction: call.direction === "incoming" ? "incoming" : "outgoing",
      status,
      durationMs: duration
    };

    api("/api/calls", { method: "POST", body: JSON.stringify(payload) })
      .then(() => { if (state.view === "calls") loadCalls(); })
      .catch(() => { /* historique non enregistré, sans conséquence pour l'appel */ });
  }

  function cleanupCall() {
    stopRingtone();
    clearTimeout(call.ringTimer);
    call.ringTimer = null;
    earlyIce.clear();
    logCallSummary();

    if (call.pc) {
      try { call.pc.close(); } catch { /* ignoré */ }
    }
    if (call.localStream) call.localStream.getTracks().forEach((track) => track.stop());

    clearInterval(call.timer);
    call.timer = null;
    call.pc = null;
    call.localStream = null;
    call.callId = null;
    call.peerId = null;
    call.kind = "audio";
    call.incoming = false;
    call.remoteSet = false;
    call.pendingCandidates = [];
    call.pendingOffer = null;
    call.startedAt = 0;
    call.muted = false;
    call.cameraOff = false;
    call.direction = "outgoing";
    call.connected = false;
    call.declined = false;

    els.callLayer.classList.add("hidden");
    els.callRemoteVideo.srcObject = null;
    els.callLocalVideo.srcObject = null;
    els.callRemoteVideo.classList.add("hidden");
    els.callRemoteVideo.classList.remove("audio-only");
    els.callLocalVideo.classList.add("hidden");
    els.callIdentity.classList.remove("hidden");
  }

  function toggleMute() {
    if (!call.localStream) return;
    call.muted = !call.muted;
    call.localStream.getAudioTracks().forEach((track) => { track.enabled = !call.muted; });
    els.callMuteBtn.classList.toggle("active", call.muted);
    els.callMuteBtn.title = call.muted ? "Réactiver le micro" : "Couper le micro";
  }

  function toggleCamera() {
    if (!call.localStream) return;
    call.cameraOff = !call.cameraOff;
    call.localStream.getVideoTracks().forEach((track) => { track.enabled = !call.cameraOff; });
    els.callCamBtn.classList.toggle("active", call.cameraOff);
    els.callCamBtn.title = call.cameraOff ? "Réactiver la caméra" : "Couper la caméra";
  }

  async function startCallTo(peerId, kind) {
    if (!peerId) return;
    if (state.activePeerId !== peerId) await openConversation(peerId);
    await startCall(kind);
  }

  async function startCallWithAny(kind) {
    const active = activePeer();
    if (active && active.online && !active.blockChat) {
      await startCall(kind);
      return;
    }

    const target = state.peers.find((peer) => peer.online && !peer.blockChat)
      || state.conversations.find((item) => item.online && !item.blockChat);

    if (!target) {
      toast("Aucun poste joignable : ouvrez l'onglet Contacts.", "error");
      return;
    }

    await startCallTo(target.peerId, kind);
  }

  // ------------------------------------------------------------- WebSocket

  function connectSocket() {
    try {
      const protocol = location.protocol === "https:" ? "wss:" : "ws:";
      const socket = new WebSocket(`${protocol}//${location.host}/ws`);
      state.socket = socket;

      socket.onopen = () => {
        state.socketOpen = true;
      };

      socket.onmessage = async (event) => {
        let payload;
        try { payload = JSON.parse(event.data); } catch { return; }

        if (payload.type === "message" || payload.type === "messageUpdated") {
          if (payload.type === "message") notifyNewMessage(payload.message, payload.peerId);
          if (payload.peerId === state.activePeerId) {
            await loadMessages(state.activePeerId);
          }
          await loadConversations();
        }

        if (payload.type === "messageDeleted") {
          if (payload.peerId === state.activePeerId) {
            await loadMessages(state.activePeerId);
          }
          await loadConversations();
        }

        if (payload.type === "typing") {
          markTyping(payload.peerId, payload.typing === true);
        }

        if (payload.type === "call") {
          handleCallSignal(payload);
        }

        if (payload.type === "peerAvatar") {
          await Promise.all([loadPeers(), loadConversations()]);
          if (payload.peerId === state.activePeerId) {
            const peer = activePeer();
            if (peer) setAvatar(els.peerAvatar, peer.peerId, payload.version);
          }
        }

        if (payload.type === "status" || payload.type === "statusViews") {
          await loadStatuses();
          if (!els.statusViewer.classList.contains("hidden") && statusState.viewer.mine) {
            const current = statusState.mine[statusState.viewer.index];
            if (current) {
              statusState.viewer.items = statusState.mine.slice();
              statusState.viewer.index = Math.min(statusState.viewer.index, statusState.mine.length - 1);
              renderStatusViewer();
            }
          }
        }

        if (payload.type === "blocks") {
          await Promise.all([loadPeers(), loadConversations(), loadBlocks()]);
        }

        if (payload.type === "me" && state.me) {
          if (payload.displayName) {
            state.me.displayName = payload.displayName;
            els.myName.textContent = payload.displayName;
          }
          if (payload.avatarVersion) {
            state.me.avatarVersion = payload.avatarVersion;
            setAvatar(els.myAvatar, "me", payload.avatarVersion, true);
          }
        }
      };

      socket.onclose = () => {
        state.socketOpen = false;
        state.socket = null;
        setTimeout(connectSocket, 3000);
      };

      socket.onerror = () => {
        state.socketOpen = false;
      };
    } catch {
      state.socketOpen = false;
    }
  }

  // ---------------------------------------------------------------- Modale

  function openRename({ title, text, value, onSave }) {
    state.renameTarget = { onSave };
    els.modalTitle.textContent = title;
    els.modalText.textContent = text;
    els.modalInput.value = value || "";
    els.modal.classList.remove("hidden");
    els.modalInput.focus();
    els.modalInput.select();
  }

  function closeRename() {
    state.renameTarget = null;
    els.modal.classList.add("hidden");
  }

  async function saveRename() {
    if (!state.renameTarget) return closeRename();
    const value = els.modalInput.value.trim();
    if (!value) {
      toast("Le nom ne peut pas être vide.", "error");
      return;
    }

    const target = state.renameTarget;
    closeRename();
    try {
      await target.onSave(value);
      await Promise.all([loadMe(), loadPeers(), loadConversations()]);
      if (state.activePeerId) await openConversation(state.activePeerId);
    } catch (error) {
      toast(error.message, "error");
    }
  }

  // --------------------------------------------------------------- Events

  function autosize() {
    const input = els.messageInput;
    input.style.height = "auto";
    const nextHeight = Math.min(input.scrollHeight, 132);
    // scrollHeight vaut 0 tant que le panneau est masqué : on ne fige pas la hauteur.
    if (nextHeight > 0) input.style.height = `${nextHeight}px`;
    input.style.overflowY = input.scrollHeight > 132 ? "auto" : "hidden";
  }

  async function cancelMessage(messageId) {
    if (!messageId) return;
    try {
      await api(`/api/messages/${encodeURIComponent(messageId)}/cancel`, { method: "POST" });
      toast("Envoi annulé : le message ne sera pas distribué.", "success");
    } catch (error) {
      toast(error.message, "error");
    }
    if (state.activePeerId) await loadMessages(state.activePeerId);
    await loadConversations();
  }

  function bindEvents() {
    document.querySelectorAll("[data-view]").forEach((button) => {
      button.addEventListener("click", () => setView(button.dataset.view));
    });

    [els.searchInput, els.callsSearch, els.contactsSearch].forEach((input) => {
      if (!input) return;
      input.addEventListener("input", () => {
        state.filter = input.value;
        renderPeers();
        renderConversations();
        renderCalls();
      });
    });

    els.chatFilters.addEventListener("click", (event) => {
      const chip = event.target.closest("[data-filter]");
      if (!chip) return;
      state.filterMode = chip.dataset.filter;
      els.chatFilters.querySelectorAll(".chip").forEach((item) => item.classList.toggle("active", item === chip));
      renderConversations();
    });

    els.newChatBtn.addEventListener("click", () => setView("contacts"));
    els.callsStartBtn.addEventListener("click", () => startCallWithAny("audio"));
    els.heroAudioBtn.addEventListener("click", () => startCallWithAny("audio"));
    els.heroVideoBtn.addEventListener("click", () => startCallWithAny("video"));

    els.callsList.addEventListener("click", (event) => {
      const start = event.target.closest("[data-call-start]");
      if (start) {
        event.stopPropagation();
        startCallTo(start.dataset.callStart, start.dataset.callKind || "audio");
        return;
      }
      const remove = event.target.closest("[data-call-delete]");
      if (remove) {
        event.stopPropagation();
        deleteCall(remove.dataset.callDelete);
        return;
      }
      const row = event.target.closest("[data-call-peer]");
      if (row) openConversation(row.dataset.callPeer);
    });

    els.messages.addEventListener("click", (event) => {
      const cancel = event.target.closest("[data-cancel]");
      if (cancel) {
        cancelMessage(cancel.dataset.cancel);
        return;
      }

      const menu = event.target.closest("[data-message-menu]");
      if (menu) {
        event.stopPropagation();
        openMessageMenu(menu.dataset.messageMenu, menu);
        return;
      }

      const translationToggle = event.target.closest("[data-translation-toggle]");
      if (translationToggle) {
        toggleMessageTranslation(translationToggle.dataset.translationToggle);
        return;
      }

      const forward = event.target.closest("[data-forward]");
      if (forward) {
        openForward(forward.dataset.forward);
        return;
      }

      const retry = event.target.closest("[data-call-retry]");
      if (retry) {
        startCallTo(retry.dataset.callRetry, retry.dataset.callKind || "audio");
        return;
      }

      const pollVote = event.target.closest("[data-poll-vote]");
      if (pollVote) {
        votePoll(pollVote.dataset.pollVote, pollVote.dataset.pollOption);
        return;
      }

      const image = event.target.closest("[data-image-open]");
      if (image) {
        const message = findMessageById(image.dataset.imageOpen);
        if (message) openImageViewer(message);
        return;
      }

      const open = event.target.closest("[data-file-open]");
      if (open) {
        const message = findMessageById(open.dataset.fileOpen);
        if (message) openMessageFile(message);
        return;
      }

      const download = event.target.closest("[data-file-download]");
      if (download) {
        const message = findMessageById(download.dataset.fileDownload);
        if (message) downloadMessageFile(message, download.dataset.fileSave === "1");
      }
    });

    // Recherche dans la conversation (loupe, Entrée / Maj+Entrée, Échap).
    els.chatSearchBtn.addEventListener("click", openChatSearch);
    els.chatSearchClose.addEventListener("click", () => closeChatSearch());
    els.chatSearchPrev.addEventListener("click", () => stepSearch(-1));
    els.chatSearchNext.addEventListener("click", () => stepSearch(1));
    els.chatSearchInput.addEventListener("input", () => {
      clearTimeout(state.search.timer);
      state.search.timer = setTimeout(runChatSearch, 240);
    });
    els.chatSearchInput.addEventListener("keydown", (event) => {
      if (event.key === "Escape") {
        event.preventDefault();
        closeChatSearch();
        return;
      }
      if (event.key === "Enter") {
        event.preventDefault();
        if (state.search.results.length > 0) stepSearch(event.shiftKey ? -1 : 1);
        else runChatSearch();
      }
    });

    // Visualiseur d'image intégré.
    els.viewerClose.addEventListener("click", closeImageViewer);
    els.viewerStage.addEventListener("click", (event) => {
      if (event.target === els.viewerStage) closeImageViewer();
    });
    els.viewerDownload.addEventListener("click", () => {
      if (viewerMessage) downloadMessageFile(viewerMessage, Boolean(downloadRecord(viewerMessage)));
    });
    els.viewerOpen.addEventListener("click", () => {
      if (viewerMessage) openImageExternally(viewerMessage);
    });
    document.addEventListener("keydown", (event) => {
      if (els.imageViewer.classList.contains("hidden")) return;
      if (event.key === "Escape") closeImageViewer();
    });

    els.peerList.addEventListener("click", (event) => {
      const row = event.target.closest("[data-peer]");
      if (row) openConversation(row.dataset.peer);
    });

    els.conversationList.addEventListener("click", (event) => {
      const row = event.target.closest("[data-peer]");
      if (row) openConversation(row.dataset.peer);
    });

    els.backBtn.addEventListener("click", () => {
      closeChatSearch(false);
      els.app.classList.remove("chat-open");
      state.activePeerId = null;
      updateMain();
      renderPeers();
      renderConversations();
    });

    els.editNameBtn.addEventListener("click", () => {
      openRename({
        title: "Mon nom",
        text: "Ce nom est affiché aux autres postes du réseau.",
        value: state.me ? state.me.displayName : "",
        onSave: (name) => api("/api/me", { method: "PUT", body: JSON.stringify({ name }) })
      });
    });

    els.contactMenuBtn.addEventListener("click", openContactMenu);
    els.menuClose.addEventListener("click", closeContactMenu);
    els.contactMenu.addEventListener("click", (event) => {
      if (event.target === els.contactMenu) closeContactMenu();
    });
    els.menuBlockChat.addEventListener("click", () => toggleBlock("chat"));
    els.menuBlockStatus.addEventListener("click", () => toggleBlock("status"));
    els.unblockChatBtn.addEventListener("click", async () => {
      if (state.activePeerId) await setBlock(state.activePeerId, { chat: false });
    });
    els.menuRename.addEventListener("click", () => {
      closeContactMenu();
      beginRenameContact();
    });

    els.blockedList.addEventListener("click", (event) => {
      const chat = event.target.closest("[data-unblock-chat]");
      if (chat) {
        setBlock(chat.dataset.unblockChat, { chat: false });
        return;
      }
      const status = event.target.closest("[data-unhide-status]");
      if (status) setBlock(status.dataset.unhideStatus, { status: false });
    });

    els.sendBtn.addEventListener("click", sendText);

    // Traduction : conversation entière et brouillon avant envoi.
    if (els.chatTranslateBtn) els.chatTranslateBtn.addEventListener("click", toggleConversationTranslation);
    if (els.composerTranslateBtn) els.composerTranslateBtn.addEventListener("click", translateDraft);
    if (els.messageMenu) {
      els.messageMenu.addEventListener("click", (event) => {
        const item = event.target.closest("[data-message-action]");
        if (!item) return;
        const messageId = els.messageMenu.dataset.messageId;
        const action = item.dataset.messageAction;
        closeMessageMenu();
        handleMessageMenuAction(action, messageId);
      });
    }

    els.messageInput.addEventListener("keydown", (event) => {
      if (event.key === "Enter" && !event.shiftKey) {
        event.preventDefault();
        sendText();
      }
    });
    els.messageInput.addEventListener("input", () => {
      autosize();
      if (els.messageInput.value.trim()) notifyTyping();
      else stopTyping();
    });

    els.imageInput.addEventListener("change", () => {
      const file = els.imageInput.files[0];
      els.imageInput.value = "";
      if (file) sendMediaFile(file);
    });

    els.videoInput.addEventListener("change", () => {
      const file = els.videoInput.files[0];
      els.videoInput.value = "";
      if (!file) return;
      const video = document.createElement("video");
      video.preload = "metadata";
      const objectUrl = URL.createObjectURL(file);
      video.onloadedmetadata = () => {
        const durationMs = Math.round((video.duration || 0) * 1000);
        URL.revokeObjectURL(objectUrl);
        uploadFile(file, "video", durationMs);
      };
      video.onerror = () => {
        URL.revokeObjectURL(objectUrl);
        uploadFile(file, "video");
      };
      video.src = objectUrl;
    });

    els.fileInput.addEventListener("change", () => {
      const file = els.fileInput.files[0];
      els.fileInput.value = "";
      if (file) uploadFile(file, kindForFile(file));
    });

    els.recordBtn.addEventListener("click", toggleRecording);
    els.stopRecordBtn.addEventListener("click", () => stopRecording(true));
    els.cancelRecordBtn.addEventListener("click", () => stopRecording(false));

    els.myAvatar.addEventListener("click", openProfile);
    els.avatarInput.addEventListener("change", () => {
      const file = els.avatarInput.files[0];
      els.avatarInput.value = "";
      if (file) uploadAvatar(file);
    });

    els.audioCallBtn.addEventListener("click", () => startCall("audio"));
    els.videoCallBtn.addEventListener("click", () => startCall("video"));
    els.callMuteBtn.addEventListener("click", toggleMute);
    els.callCamBtn.addEventListener("click", toggleCamera);
    els.callAcceptBtn.addEventListener("click", acceptCall);
    els.callHangupBtn.addEventListener("click", () => {
      if (call.incoming) declineCall();
      else endCall(true, "Appel terminé.");
    });

    // ---------------------------------------------------------- Statuts
    els.myStatusRow.addEventListener("click", openMyStatuses);
    els.myStatusAdd.addEventListener("click", (event) => {
      event.stopPropagation();
      openStatusComposer();
    });
    els.newStatusBtn.addEventListener("click", openStatusComposer);
    els.heroStatusBtn.addEventListener("click", openStatusComposer);
    els.statusList.addEventListener("click", (event) => {
      const row = event.target.closest("[data-status-peer]");
      if (row) openPeerStatuses(row.dataset.statusPeer);
    });
    els.statusViewedList.addEventListener("click", (event) => {
      const row = event.target.closest("[data-status-peer]");
      if (row) openPeerStatuses(row.dataset.statusPeer);
    });

    els.statusComposer.addEventListener("click", (event) => {
      if (event.target === els.statusComposer) closeStatusComposer();
    });
    els.statusComposeClose.addEventListener("click", closeStatusComposer);
    els.statusPublish.addEventListener("click", publishStatus);
    els.statusTabs.addEventListener("click", (event) => {
      const tab = event.target.closest("[data-status-tab]");
      if (!tab) return;
      if (statusState.file && statusState.tab !== tab.dataset.statusTab) {
        statusState.file = null;
        if (statusState.previewUrl) URL.revokeObjectURL(statusState.previewUrl);
        statusState.previewUrl = null;
        els.statusImagePreview.classList.add("hidden");
        els.statusVideoPreview.classList.add("hidden");
      }
      setStatusTab(tab.dataset.statusTab);
    });
    els.statusColors.addEventListener("click", (event) => {
      const color = event.target.closest("[data-status-color]");
      if (!color) return;
      statusState.background = color.dataset.statusColor;
      els.statusTextPreview.style.background = statusState.background;
      renderStatusColors();
    });
    els.statusPickFile.addEventListener("click", () => els.statusFileInput.click());
    els.statusFileInput.addEventListener("change", () => {
      const file = els.statusFileInput.files[0];
      els.statusFileInput.value = "";
      if (file) setStatusFile(file);
    });

    els.statusViewerClose.addEventListener("click", closeStatusViewer);
    els.statusPrev.addEventListener("click", previousStatus);
    els.statusNext.addEventListener("click", nextStatus);
    els.statusViewsBtn.addEventListener("click", () => {
      els.statusViewersPanel.classList.toggle("hidden");
    });
    els.statusViewersClose.addEventListener("click", () => els.statusViewersPanel.classList.add("hidden"));
    if (els.statusPauseTop) els.statusPauseTop.addEventListener("click", toggleStatusPaused);
    if (els.statusOpenViewersTop) {
      els.statusOpenViewersTop.addEventListener("click", () => els.statusViewersPanel.classList.remove("hidden"));
    }
    document.addEventListener("keydown", (event) => {
      if (els.statusViewer.classList.contains("hidden")) return;
      if (event.key === "Escape") closeStatusViewer();
      if (event.key === "ArrowRight") nextStatus();
      if (event.key === "ArrowLeft") previousStatus();

      // Espace met le statut en pause (ou le reprend), comme dans WhatsApp.
      if (event.code === "Space" && !event.repeat
          && !["INPUT", "TEXTAREA", "SELECT"].includes(event.target.tagName)) {
        event.preventDefault();
        toggleStatusPaused();
      }
    });

    els.modalSave.addEventListener("click", saveRename);
    els.modalCancel.addEventListener("click", closeRename);
    els.modalInput.addEventListener("keydown", (event) => {
      if (event.key === "Enter") saveRename();
      if (event.key === "Escape") closeRename();
    });
    els.modal.addEventListener("click", (event) => {
      if (event.target === els.modal) closeRename();
    });

    window.addEventListener("dragover", (event) => event.preventDefault());
    window.addEventListener("drop", (event) => {
      event.preventDefault();
      if (!state.activePeerId) return;
      const file = event.dataTransfer && event.dataTransfer.files[0];
      if (!file) return;
      uploadFile(file, kindForFile(file));
    });

    bindFeatureEvents();
  }

  // ============================================ Menus, profil et pièces jointes

  function beginRenameContact() {
    if (!state.activePeerId) return;
    const peer = activePeer();
    const peerId = state.activePeerId;
    openRename({
      title: "Renommer ce contact",
      text: "Le nouveau nom n'est visible que sur ce poste. Laissez vide pour revenir au nom d'origine.",
      value: peer ? peer.name : "",
      onSave: (name) => api(`/api/peers/${encodeURIComponent(peerId)}/alias`, {
        method: "PUT",
        body: JSON.stringify({ name })
      })
    });
  }

  function openPeerProfile() {
    const peer = activePeer();
    if (!peer) return;
    closeContactMenu();
    els.peerProfileName.textContent = peer.name;
    const details = [];
    if (peer.self) details.push("Messages à moi-même");
    else {
      if (peer.machineName && peer.machineName !== peer.name) details.push(peer.machineName);
      details.push(peer.online ? "en ligne" : "hors ligne");
    }
    els.peerProfileMeta.textContent = details.join(" · ");
    setAvatar(els.peerProfileAvatar, peer.self ? "me" : peer.peerId, peer.avatarVersion, peer.self);
    els.peerProfileCall.disabled = Boolean(peer.self) || !peer.online || Boolean(peer.blockChat);
    els.peerProfileVideo.disabled = Boolean(peer.self) || !peer.online || Boolean(peer.blockChat);
    els.peerProfileRename.classList.toggle("hidden", Boolean(peer.self));
    els.peerProfileModal.classList.remove("hidden");
  }

  function closeAttachMenu() {
    els.attachMenu.classList.add("hidden");
  }

  function handleAttach(action) {
    closeAttachMenu();
    if (!state.activePeerId) return;
    if (action === "media") els.imageInput.click();
    else if (action === "camera") openCamera();
    else if (action === "document") els.fileInput.click();
    else if (action === "audio") els.audioInput.click();
    else if (action === "poll") openPollComposer();
  }

  function sendMediaFile(file) {
    if (!file) return;
    const kind = file.type.startsWith("video/") ? "video" : "image";
    if (kind === "image") {
      uploadFile(file, "image");
      return;
    }

    const video = document.createElement("video");
    video.preload = "metadata";
    const objectUrl = URL.createObjectURL(file);
    const send = (durationMs) => {
      URL.revokeObjectURL(objectUrl);
      uploadFile(file, "video", durationMs);
    };
    video.onloadedmetadata = () => send(Math.round((video.duration || 0) * 1000));
    video.onerror = () => send(0);
    video.src = objectUrl;
  }

  function bindFeatureEvents() {
    // Menu « ⋯ » de la colonne Discussions.
    els.chatsMenuBtn.addEventListener("click", (event) => {
      event.stopPropagation();
      els.chatsMenu.classList.toggle("hidden");
    });
    els.chatsMenu.addEventListener("click", (event) => {
      const item = event.target.closest("[data-chats-menu]");
      if (!item) return;
      els.chatsMenu.classList.add("hidden");
      if (item.dataset.chatsMenu === "profile") openProfile();
      else if (item.dataset.chatsMenu === "theme") toggleTheme();
    });

    // Mon profil : photo, nom et thème.
    els.profileClose.addEventListener("click", closeProfile);
    els.profileModal.addEventListener("click", (event) => {
      if (event.target === els.profileModal) closeProfile();
    });
    els.profileChangeAvatar.addEventListener("click", () => els.avatarInput.click());
    els.profileRename.addEventListener("click", () => {
      closeProfile();
      if (!state.me) return;
      openRename({
        title: "Mon nom",
        text: "Ce nom est affiché aux autres postes du réseau.",
        value: state.me.displayName,
        onSave: (name) => api("/api/me", { method: "PUT", body: JSON.stringify({ name }) })
      });
    });
    els.profileAvatar.addEventListener("click", () => {
      if (!state.me || !state.me.avatarVersion) return;
      openStandaloneImage(`/api/me/avatar?v=${encodeURIComponent(state.me.avatarVersion)}`, state.me.displayName);
    });
    els.themeSwitch.addEventListener("click", (event) => {
      const button = event.target.closest("[data-theme]");
      if (button) applyTheme(button.dataset.theme);
    });

    // Langues : changer un champ ne bloque jamais ; si la langue choisie est
    // déjà celle de l'autre champ, les deux sont échangées.
    if (els.sourceLangSelect) {
      els.sourceLangSelect.addEventListener("change", () =>
        saveTranslationLanguage("source", els.sourceLangSelect.value));
    }
    if (els.targetLangSelect) {
      els.targetLangSelect.addEventListener("change", () =>
        saveTranslationLanguage("target", els.targetLangSelect.value));
    }
    if (els.openLanguagesBtn) {
      els.openLanguagesBtn.addEventListener("click", openTranslationManager);
    }
    if (els.clearTranslationCacheBtn) {
      els.clearTranslationCacheBtn.addEventListener("click", clearTranslationCache);
    }
    if (els.translationClose) els.translationClose.addEventListener("click", closeTranslationManager);
    if (els.translationModal) {
      els.translationModal.addEventListener("click", (event) => {
        if (event.target === els.translationModal) closeTranslationManager();
      });
      els.translationModal.addEventListener("click", (event) => {
        const tab = event.target.closest("[data-translation-tab]");
        if (tab) {
          translationState.tab = tab.dataset.translationTab;
          els.translationModal.querySelectorAll("[data-translation-tab]").forEach((button) => {
            button.classList.toggle("active", button === tab);
          });
          renderTranslationModels();
          return;
        }
        const install = event.target.closest("[data-install-from]");
        if (install) {
          installTranslationModel(install.dataset.installFrom, install.dataset.installTo);
          return;
        }
        const remove = event.target.closest("[data-remove-from]");
        if (remove) {
          removeTranslationModel(remove.dataset.removeFrom, remove.dataset.removeTo);
        }
      });
    }
    if (els.translationCancelBtn) {
      els.translationCancelBtn.addEventListener("click", cancelTranslationJob);
    }
    if (els.saveTranslationPathsBtn) {
      els.saveTranslationPathsBtn.addEventListener("click", saveTranslationPaths);
    }

    if (els.translateCancel) els.translateCancel.addEventListener("click", closeTranslatePreview);
    if (els.translateRestore) {
      els.translateRestore.addEventListener("click", () => {
        if (!translationState.pending) return;
        els.translateText.value = translationState.pending.original;
        els.translatePreviewLangs.textContent = "Texte original — aucune traduction";
        els.translateText.focus();
      });
    }
    if (els.translateSend) els.translateSend.addEventListener("click", sendTranslatedDraft);
    if (els.translatePreviewModal) {
      els.translatePreviewModal.addEventListener("click", (event) => {
        if (event.target === els.translatePreviewModal) closeTranslatePreview();
      });
    }

    // Menu de la discussion.
    els.menuViewProfile.addEventListener("click", openPeerProfile);
    els.menuShared.addEventListener("click", () => {
      closeContactMenu();
      openShared();
    });
    els.menuClear.addEventListener("click", clearConversation);
    els.menuDelete.addEventListener("click", deleteConversation);
    els.menuAvatar.addEventListener("click", () => {
      const peer = activePeer();
      if (!peer) return;
      if (peer.self) {
        if (state.me?.avatarVersion) openStandaloneImage(`/api/me/avatar?v=${encodeURIComponent(state.me.avatarVersion)}`, state.me.displayName);
      } else if (peer.avatarVersion) {
        openPeerAvatarViewer(peer.peerId, peer.name, peer.avatarVersion);
      } else {
        toast("Ce contact n'a pas encore de photo de profil.");
      }
    });

    // Profil d'un contact.
    els.peerProfileClose.addEventListener("click", () => els.peerProfileModal.classList.add("hidden"));
    els.peerProfileModal.addEventListener("click", (event) => {
      if (event.target === els.peerProfileModal) els.peerProfileModal.classList.add("hidden");
    });
    els.peerProfileAvatar.addEventListener("click", () => {
      const peer = activePeer();
      if (!peer) return;
      if (peer.self) {
        if (state.me?.avatarVersion) openStandaloneImage(`/api/me/avatar?v=${encodeURIComponent(state.me.avatarVersion)}`, state.me.displayName);
      } else if (peer.avatarVersion) {
        openPeerAvatarViewer(peer.peerId, peer.name, peer.avatarVersion);
      } else {
        toast("Ce contact n'a pas encore de photo de profil.");
      }
    });
    els.peerProfileMessage.addEventListener("click", () => {
      els.peerProfileModal.classList.add("hidden");
      els.messageInput.focus();
    });
    els.peerProfileCall.addEventListener("click", () => {
      els.peerProfileModal.classList.add("hidden");
      startCall("audio");
    });
    els.peerProfileVideo.addEventListener("click", () => {
      els.peerProfileModal.classList.add("hidden");
      startCall("video");
    });
    els.peerProfileRename.addEventListener("click", () => {
      els.peerProfileModal.classList.add("hidden");
      beginRenameContact();
    });

    // Pièce jointe unique : photos/vidéos, caméra, document, audio, sondage.
    els.attachBtn.addEventListener("click", (event) => {
      event.stopPropagation();
      toggleAttachMenu();
    });
    els.attachMenu.addEventListener("click", (event) => {
      const item = event.target.closest("[data-attach]");
      if (item) handleAttach(item.dataset.attach);
    });
    els.viewOnceToggle.addEventListener("click", (event) => {
      event.stopPropagation();
      state.viewOnce = !state.viewOnce;
      updateViewOnceToggle();
    });
    els.viewOnceClose.addEventListener("click", closeViewOnceViewer);
    els.viewOnceViewer.addEventListener("click", (event) => {
      if (event.target === els.viewOnceViewer) closeViewOnceViewer();
    });
    els.messages.addEventListener("click", (event) => {
      const button = event.target.closest("[data-view-once]");
      if (button) openViewOnce(button.dataset.viewOnce);
    });
    els.audioInput.addEventListener("change", () => {
      const file = els.audioInput.files[0];
      els.audioInput.value = "";
      if (file) uploadFile(file, "audio");
    });

    // Caméra intégrée : photo prise puis envoyée directement.
    els.cameraCancel.addEventListener("click", closeCamera);
    els.cameraSwitch.addEventListener("click", switchCamera);
    els.cameraCapture.addEventListener("click", capturePhoto);
    els.cameraModal.addEventListener("click", (event) => {
      if (event.target === els.cameraModal) closeCamera();
    });

    // Sondage.
    els.pollCancel.addEventListener("click", () => els.pollModal.classList.add("hidden"));
    els.pollAddOption.addEventListener("click", () => {
      readPollDraft();
      if (state.pollDraft.length >= 12) return;
      state.pollDraft.push("");
      renderPollDraft(state.pollDraft.length - 1);
    });
    els.pollOptionsEdit.addEventListener("input", readPollDraft);
    els.pollSend.addEventListener("click", sendPoll);
    els.pollModal.addEventListener("click", (event) => {
      if (event.target === els.pollModal) els.pollModal.classList.add("hidden");
    });

    // Transférer un message, un fichier, une photo, une vidéo ou un lien.
    els.forwardCancel.addEventListener("click", closeForward);
    els.forwardSend.addEventListener("click", submitForward);
    els.forwardModal.addEventListener("click", (event) => {
      if (event.target === els.forwardModal) closeForward();
    });
    els.forwardList.addEventListener("click", (event) => {
      const row = event.target.closest("[data-forward-target]");
      if (!row) return;
      const peerId = row.dataset.forwardTarget;
      if (state.forward.targets.has(peerId)) state.forward.targets.delete(peerId);
      else state.forward.targets.add(peerId);
      renderForwardList();
    });

    // Médias, liens et documents partagés.
    els.sharedClose.addEventListener("click", () => els.sharedModal.classList.add("hidden"));
    els.sharedModal.addEventListener("click", (event) => {
      if (event.target === els.sharedModal) els.sharedModal.classList.add("hidden");
    });
    els.sharedTabs.addEventListener("click", (event) => {
      const chip = event.target.closest("[data-shared]");
      if (chip) setSharedTab(chip.dataset.shared);
    });
    els.sharedBody.addEventListener("click", (event) => {
      const media = event.target.closest("[data-shared-media]");
      if (media) {
        openSharedMedia(media.dataset.sharedMedia);
        return;
      }
      const document = event.target.closest("[data-shared-doc]");
      if (document) {
        const item = findSharedItem(document.dataset.sharedDoc);
        if (item) openMessageFile(item);
        return;
      }
      const link = event.target.closest("[data-external-link]");
      if (link) {
        event.preventDefault();
        const url = link.dataset.externalLink;
        if (desktop) desktop.send("app.openExternal", { url });
        else window.open(url, "_blank", "noopener");
      }
    });

    // Statuts : j'aime, commentaires, pause, confidentialité par statut.
    els.statusLikeBtn.addEventListener("click", toggleStatusLike);
    els.statusCommentSend.addEventListener("click", sendStatusComment);
    els.statusCommentInput.addEventListener("keydown", (event) => {
      if (event.key === "Enter") {
        event.preventDefault();
        sendStatusComment();
      }
    });
    els.statusPrivacy.addEventListener("change", () => {
      statusState.privacyPeers.clear();
      renderPrivacyPeers();
    });
    els.statusPrivacyPeers.addEventListener("change", (event) => {
      const box = event.target.closest("[data-privacy-peer]");
      if (!box) return;
      if (box.checked) statusState.privacyPeers.add(box.dataset.privacyPeer);
      else statusState.privacyPeers.delete(box.dataset.privacyPeer);
    });
    els.statusPrivacyEdit.addEventListener("change", renderPrivacyEditPeers);
    els.statusPrivacySave.addEventListener("click", saveStatusPrivacy);
    els.statusViewersPanel.addEventListener("click", (event) => {
      if (event.target === els.statusViewersPanel) els.statusViewersPanel.classList.add("hidden");
    });

    // Historique des appels.
    els.clearCallsBtn.addEventListener("click", clearCalls);

    // Menus fermés au clic ailleurs.
    document.addEventListener("click", (event) => {
      if (!event.target.closest("#chatsMenu") && !event.target.closest("#chatsMenuBtn")) {
        els.chatsMenu.classList.add("hidden");
      }
      if (!event.target.closest("#attachMenu") && !event.target.closest("#attachBtn")) {
        els.attachMenu.classList.add("hidden");
      }
      if (!event.target.closest("#messageMenu") && !event.target.closest(".msg-menu-btn")) {
        closeMessageMenu();
      }
    });

    document.addEventListener("keydown", (event) => {
      if (event.key === "Escape") closeMessageMenu();
    });
  }

  // ============================================ Statuts : activité et confidentialité

  function currentStatusItem() {
    const viewer = statusState.viewer;
    return viewer.items[viewer.index] || null;
  }

  function refreshViewerItems() {
    const viewer = statusState.viewer;
    if (!viewer.owner) return;
    const source = viewer.mine
      ? statusState.mine
      : (statusState.peers.find((group) => group.peerId === viewer.owner.peerId)?.statuses || []);
    if (source.length === 0) {
      closeStatusViewer();
      return;
    }
    viewer.items = source.slice();
    viewer.index = Math.min(viewer.index, viewer.items.length - 1);
    renderStatusViewer();
  }

  function renderStatusActivity(item) {
    const likes = item.likes || [];
    const comments = item.comments || [];
    const likedLocally = statusState.liked.has(item.id);
    const liked = likedLocally || Boolean(state.me && likes.some((like) => like.peerId === state.me.peerId));
    const storedCount = typeof item.likeCount === "number" ? item.likeCount : likes.length;
    const count = likedLocally && storedCount === 0 ? 1 : storedCount;

    els.statusLikeCount.textContent = String(count);
    els.statusLikeBtn.classList.toggle("liked", liked);
    els.statusLikeBtn.title = liked ? "Je n'aime plus ce statut" : "J'aime ce statut";

    const views = item.views || [];
    els.statusViewerViews.textContent = String(item.viewCount || views.length);
    if (els.statusLikesCount) els.statusLikesCount.textContent = String(count);
    if (els.statusCommentsCount) els.statusCommentsCount.textContent = String(comments.length);
    if (els.statusViewersCount) els.statusViewersCount.textContent = String(views.length);

    if (els.statusLikesList) {
      els.statusLikesList.innerHTML = likes.length === 0
        ? '<p class="empty-hint">Aucun j\'aime pour le moment.</p>'
        : likes.map((like) => `
          <div class="status-like-row">
            <span class="name">${escapeHtml(like.name || like.peerId)}</span>
            <span class="at">${escapeHtml(like.at ? formatClock(like.at) : "")}</span>
          </div>`).join("");
    }

    if (els.statusCommentsList) {
      els.statusCommentsList.innerHTML = comments.length === 0
        ? '<p class="empty-hint">Aucun commentaire pour le moment.</p>'
        : comments.map((comment) => `
          <div class="status-comment-row">
            <span class="name">${escapeHtml(comment.name || comment.peerId)}</span>
            <span class="text">${escapeHtml(comment.text)}</span>
            <span class="at">${escapeHtml(comment.at ? formatClock(comment.at) : "")}</span>
          </div>`).join("");
    }
  }

  function renderStatusPrivacyEditor(item) {
    if (!els.statusPrivacyEditWrap) return;
    els.statusPrivacyEditWrap.classList.toggle("hidden", !statusState.viewer.mine);
    if (!statusState.viewer.mine) return;
    els.statusPrivacyEdit.value = item.privacy || "all";
    renderPrivacyEditPeers();
  }

  function renderPrivacyEditPeers() {
    const item = currentStatusItem();
    if (!item) return;
    const value = els.statusPrivacyEdit.value;
    const needsList = value === "except" || value === "only";
    els.statusPrivacyEditPeers.classList.toggle("hidden", !needsList);
    if (!needsList) return;

    const selected = new Set(item.privacyList || []);
    const peers = state.peers.filter((peer) => peer.peerId !== state.me?.peerId);
    els.statusPrivacyEditPeers.innerHTML = peers.length === 0
      ? '<span class="empty-hint">Aucun contact pour le moment.</span>'
      : peers.map((peer) => `<label><input type="checkbox" data-privacy-edit-peer="${escapeHtml(peer.peerId)}"${selected.has(peer.peerId) ? " checked" : ""}> ${escapeHtml(peer.name)}</label>`).join("");
  }

  async function saveStatusPrivacy() {
    const item = currentStatusItem();
    if (!item || !statusState.viewer.mine) return;
    const privacy = els.statusPrivacyEdit.value;
    const peers = Array.from(els.statusPrivacyEditPeers.querySelectorAll("[data-privacy-edit-peer]:checked"))
      .map((box) => box.dataset.privacyEditPeer);

    try {
      await api(`/api/statuses/${encodeURIComponent(item.id)}/privacy`, {
        method: "PUT",
        body: JSON.stringify({ privacy, peers })
      });
      await loadStatuses();
      refreshViewerItems();
      toast("Confidentialité du statut enregistrée.", "success");
    } catch (error) {
      toast(error.message, "error");
    }
  }

  // ================================================== Traduction hors ligne

  function translationPairFor(message) {
    const settings = translationState.settings;
    const outgoing = message.direction === "outgoing";
    return outgoing
      ? { from: settings.sourceLang, to: settings.targetLang }
      : { from: settings.targetLang, to: settings.sourceLang };
  }

  function languageLabel(code) {
    const languages = translationState.settings.languages || [];
    const found = languages.find((language) => language.code === code);
    if (found) return found.name;
    return String(code || "").toUpperCase();
  }

  function messageTextToTranslate(message) {
    if (!message) return "";
    if (message.kind === "text") return (message.text || "").trim();
    if (message.kind === "statuscomment") {
      const data = parseMessageJson(message.text);
      return String(data.text || "").trim();
    }
    return "";
  }

  function translationHtml(message) {
    if (!messageTextToTranslate(message)) return "";
    const entry = translationState.messages.get(message.id);

    if (!entry) {
      return translationState.showAll
        ? '<div class="bubble-translation"><div class="translation-loading">🌐 Traduction en cours…</div></div>'
        : "";
    }

    if (!entry.shown) {
      return `<div class="bubble-translation"><button class="translation-show" type="button" data-translation-toggle="${escapeHtml(message.id)}">🌐 Afficher la traduction</button></div>`;
    }

    const head = `<div class="translation-head">🌐 ${escapeHtml(entry.fromName || languageLabel(entry.from))} → ${escapeHtml(entry.toName || languageLabel(entry.to))}<button type="button" data-translation-toggle="${escapeHtml(message.id)}">Masquer</button></div>`;

    if (entry.loading) {
      return `<div class="bubble-translation">${head}<div class="translation-loading">Traduction…</div></div>`;
    }

    if (entry.error) {
      return `<div class="bubble-translation">${head}<div class="translation-error">${escapeHtml(entry.error)}</div></div>`;
    }

    return `<div class="bubble-translation">${head}<div class="translation-text">${escapeHtml(entry.text)}</div></div>`;
  }

  async function translateMessage(messageId, options = {}) {
    const message = findMessageById(messageId);
    if (!message) return;

    const text = messageTextToTranslate(message);
    if (!text) {
      if (!options.quiet) toast("Ce message ne contient pas de texte à traduire.");
      return;
    }

    const pair = translationPairFor(message);
    if (pair.from === pair.to) {
      if (!options.quiet) {
        toast("Ma langue et la langue de traduction doivent être différentes : choisissez-les dans Mon profil > Langues.", "error");
      }
      return;
    }

    translationState.messages.set(messageId, {
      shown: true,
      loading: true,
      from: pair.from,
      to: pair.to,
      fromName: languageLabel(pair.from),
      toName: languageLabel(pair.to)
    });
    if (options.render !== false) renderMessages();

    try {
      const result = await api("/api/translation/translate", {
        method: "POST",
        body: JSON.stringify({ text, from: pair.from, to: pair.to })
      });
      translationState.messages.set(messageId, {
        shown: true,
        loading: false,
        text: result.text,
        from: result.from,
        to: result.to,
        fromName: result.fromName,
        toName: result.toName,
        cached: result.cached === true
      });
    } catch (error) {
      translationState.messages.set(messageId, {
        shown: true,
        loading: false,
        error: error.message,
        from: pair.from,
        to: pair.to,
        fromName: languageLabel(pair.from),
        toName: languageLabel(pair.to)
      });
      if (!options.quiet) toast(error.message, "error");
    }

    if (options.render !== false) renderMessages();
  }

  function toggleMessageTranslation(messageId) {
    const entry = translationState.messages.get(messageId);
    if (!entry) {
      translateMessage(messageId);
      return;
    }
    entry.shown = !entry.shown;
    renderMessages();
  }

  async function toggleConversationTranslation() {
    if (translationState.showAll) {
      translationState.showAll = false;
      translationState.messages.forEach((entry) => { entry.shown = false; });
      updateChatTranslateButton();
      renderMessages();
      return;
    }

    const settings = translationState.settings;
    if (settings.sourceLang === settings.targetLang) {
      toast("Ma langue et la langue de traduction doivent être différentes : choisissez-les dans Mon profil > Langues.", "error");
      return;
    }

    translationState.showAll = true;
    updateChatTranslateButton();
    renderMessages();

    const targets = state.messages.filter((message) =>
      messageTextToTranslate(message) && !translationState.messages.has(message.id));
    if (targets.length === 0) {
      toast("Les messages de cette conversation sont déjà traduits.");
      return;
    }

    toast(`Traduction de ${targets.length} message(s)…`);
    els.chatTranslateBtn.classList.add("busy");
    let done = 0;
    for (const message of targets) {
      await translateMessage(message.id, { render: false, quiet: true });
      done += 1;
      if (done % 3 === 0) renderMessages();
    }
    els.chatTranslateBtn.classList.remove("busy");
    renderMessages();
    toast(`${done} message(s) traduit(s) hors ligne.`, "success");
  }

  function updateChatTranslateButton() {
    if (!els.chatTranslateBtn) return;
    els.chatTranslateBtn.classList.toggle("active", translationState.showAll);
    els.chatTranslateBtn.title = translationState.showAll
      ? "Masquer les traductions de la conversation"
      : "Traduire la conversation";
  }

  async function translateDraft() {
    const text = els.messageInput.value.trim();
    if (!text) {
      toast("Écrivez d'abord un message.");
      return;
    }

    const settings = translationState.settings;
    if (settings.sourceLang === settings.targetLang) {
      toast("Ma langue et la langue de traduction doivent être différentes : choisissez-les dans Mon profil > Langues.", "error");
      openProfile();
      return;
    }

    els.composerTranslateBtn.classList.add("busy");
    try {
      const result = await api("/api/translation/translate", {
        method: "POST",
        body: JSON.stringify({
          text,
          from: settings.sourceLang,
          to: settings.targetLang
        })
      });

      translationState.pending = { original: text, translated: result.text };
      els.translateOriginal.textContent = text;
      els.translateText.value = result.text;
      els.translatePreviewLangs.textContent =
        `${result.fromName || languageLabel(result.from)} → ${result.toName || languageLabel(result.to)} · ${result.cached ? "cache local" : "traduit hors ligne"}`;
      els.translatePreviewModal.classList.remove("hidden");
      els.translateText.focus();
    } catch (error) {
      toast(error.message, "error");
    } finally {
      els.composerTranslateBtn.classList.remove("busy");
    }
  }

  function closeTranslatePreview() {
    els.translatePreviewModal.classList.add("hidden");
    translationState.pending = null;
  }

  async function sendTranslatedDraft() {
    const value = els.translateText.value.trim();
    if (!value) {
      toast("La traduction est vide.", "error");
      return;
    }
    els.messageInput.value = value;
    autosize();
    closeTranslatePreview();
    await sendText();
  }

  // ------------------------------------------------- Langues et modèles

  function fillTranslationSelects() {
    if (!els.sourceLangSelect || !els.targetLangSelect) return;
    const languages = translationState.settings.languages && translationState.settings.languages.length > 0
      ? translationState.settings.languages
      : [
          { code: "fr", name: "Français" },
          { code: "en", name: "Anglais" },
          { code: "es", name: "Espagnol" },
          { code: "de", name: "Allemand" }
        ];

    const html = languages
      .map((language) => `<option value="${escapeHtml(language.code)}">${escapeHtml(language.name)}</option>`)
      .join("");

    if (els.sourceLangSelect.innerHTML !== html) {
      els.sourceLangSelect.innerHTML = html;
      els.targetLangSelect.innerHTML = html;
    }

    els.sourceLangSelect.value = translationState.settings.sourceLang || "fr";
    els.targetLangSelect.value = translationState.settings.targetLang || "en";
  }

  function renderTranslationSummary() {
    const status = translationState.status;
    const cache = translationState.cache || { count: 0 };
    const available = Boolean(status && status.available);
    const pairs = status && Array.isArray(status.installedPairs) ? status.installedPairs.length : 0;

    if (els.translationState) {
      els.translationState.textContent = available
        ? `Hors ligne prêt · ${pairs} paire(s)`
        : "Moteur non installé";
    }
    if (els.translationEngineHint) {
      els.translationEngineHint.textContent = available
        ? `Argos Translate ${status.version || ""} · ${pairs} paire(s) installée(s)`.trim()
        : "Argos Translate non détecté · cliquez pour installer";
    }
    if (els.translationCacheHint) {
      els.translationCacheHint.textContent = cache.count > 0
        ? `${cache.count} traduction(s) en cache`
        : "Aucune traduction en cache";
    }
    if (els.translationEngine && els.translationEngineText) {
      els.translationEngine.classList.toggle("online", available);
      els.translationEngineText.textContent = available
        ? `Argos Translate ${status.version || ""} · traductions hors ligne disponibles`
        : (status && status.error) || "Argos Translate n'est pas installé sur ce poste.";
    }
  }

  async function loadTranslationSettings() {
    try {
      const [settings, status, cache] = await Promise.all([
        api("/api/translation/settings"),
        api("/api/translation/status").catch(() => null),
        api("/api/translation/cache").catch(() => null)
      ]);

      translationState.settings = settings;
      if (status) translationState.status = status;
      if (cache) translationState.cache = cache;
      fillTranslationSelects();
      renderTranslationSummary();
    } catch {
      /* le serveur local n'est pas encore prêt */
    }
  }

  async function saveTranslationLanguage(which, value) {
    const settings = translationState.settings;
    const patch = {
      sourceLang: settings.sourceLang,
      targetLang: settings.targetLang
    };

    // Choisir la langue déjà utilisée par l'autre champ ne bloque plus :
    // les deux langues sont échangées automatiquement.
    if (which === "source") {
      patch.sourceLang = value;
      if (value === settings.targetLang) patch.targetLang = settings.sourceLang;
    } else {
      patch.targetLang = value;
      if (value === settings.sourceLang) patch.sourceLang = settings.targetLang;
    }

    try {
      const updated = await api("/api/translation/settings", {
        method: "PUT",
        body: JSON.stringify(patch)
      });
      translationState.settings = updated;
      fillTranslationSelects();
      toast("Langues enregistrées.", "success");
    } catch (error) {
      fillTranslationSelects();
      toast(error.message, "error");
    }
  }

  async function openTranslationManager() {
    translationState.tab = "installed";
    els.translationModal.classList.remove("hidden");
    els.translationModal.querySelectorAll("[data-translation-tab]").forEach((button) => {
      button.classList.toggle("active", button.dataset.translationTab === "installed");
    });
    await loadTranslationSettings();
    fillTranslationPathInputs();
    renderTranslationSummary();
    await loadTranslationModels();
  }

  function fillTranslationPathInputs() {
    const settings = translationState.settings || {};
    if (els.argosPathInput) els.argosPathInput.value = settings.argosPath || "";
    if (els.pythonPathInput) els.pythonPathInput.value = settings.pythonPath || "";
  }

  async function saveTranslationPaths() {
    const settings = translationState.settings || {};
    try {
      const updated = await api("/api/translation/settings", {
        method: "PUT",
        body: JSON.stringify({
          sourceLang: settings.sourceLang,
          targetLang: settings.targetLang,
          argosPath: els.argosPathInput ? els.argosPathInput.value.trim() : "",
          pythonPath: els.pythonPathInput ? els.pythonPathInput.value.trim() : ""
        })
      });
      translationState.settings = updated;
      fillTranslationPathInputs();
      toast("Emplacements enregistrés. Le moteur va être relancé.", "success");
      await loadTranslationSettings();
      renderTranslationSummary();
      await loadTranslationModels();
    } catch (error) {
      toast(error.message, "error");
    }
  }

  function closeTranslationManager() {
    clearTimeout(translationState.pollTimer);
    els.translationModal.classList.add("hidden");
  }

  async function loadTranslationModels() {
    if (translationState.loadingModels) return;
    translationState.loadingModels = true;
    els.translationBody.innerHTML = '<p class="translation-empty">Recherche des modèles Argos…</p>';
    try {
      const models = await api("/api/translation/models");
      translationState.models = models;
      renderTranslationModels();
    } catch (error) {
      translationState.models = null;
      els.translationBody.innerHTML = `<p class="translation-empty">${escapeHtml(error.message)}</p>`;
    } finally {
      translationState.loadingModels = false;
    }
  }

  function renderTranslationModels() {
    const models = translationState.models;
    if (!models) return;

    const installed = Array.isArray(models.installed) ? models.installed : [];
    const available = Array.isArray(models.available) ? models.available : [];
    const tab = translationState.tab || "installed";

    if (els.translationNote) {
      els.translationNote.textContent = models.ok === false
        ? models.error || "Liste des modèles indisponible."
        : "Astuce : installez Anglais → Français et Français → Anglais pour traduire dans les deux sens.";
    }

    if (tab === "installed") {
      els.translationBody.innerHTML = installed.length === 0
        ? '<p class="translation-empty">Aucun modèle installé. Ouvrez l’onglet « Disponibles » pour en télécharger un.</p>'
        : installed.map((pair) => `
          <div class="translation-row">
            <span class="translation-pair">${escapeHtml(pair.fromName || languageLabel(pair.from))} → ${escapeHtml(pair.toName || languageLabel(pair.to))}<small>Installé sur ce poste · fonctionne sans Internet</small></span>
            <button class="text-btn danger" type="button" data-remove-from="${escapeHtml(pair.from)}" data-remove-to="${escapeHtml(pair.to)}">Supprimer</button>
          </div>`).join("");
      return;
    }

    if (available.length === 0) {
      els.translationBody.innerHTML = '<p class="translation-empty">Aucun modèle disponible. Vérifiez la connexion Internet puis rouvrez cette fenêtre.</p>';
      return;
    }

    const installedKeys = new Set(installed.map((pair) => `${pair.from}-${pair.to}`));
    const sorted = available.slice().sort((a, b) => {
      const score = (pair) => (pair.from === "fr" && pair.to === "en" ? 0 : pair.from === "en" && pair.to === "fr" ? 1 : 2);
      return score(a) - score(b) || String(a.from).localeCompare(String(b.from)) || String(a.to).localeCompare(String(b.to));
    });

    els.translationBody.innerHTML = sorted.map((pair) => {
      const key = `${pair.from}-${pair.to}`;
      const isInstalled = installedKeys.has(key);
      const size = typeof pair.sizeBytes === "number"
        ? ` · ${formatBytes(pair.sizeBytes)}`
        : "";
      return `
        <div class="translation-row">
          <span class="translation-pair">${escapeHtml(pair.fromName || languageLabel(pair.from))} → ${escapeHtml(pair.toName || languageLabel(pair.to))}<small>Modèle hors ligne${escapeHtml(size)}</small></span>
          ${isInstalled
            ? '<span class="translation-installed">Installé</span>'
            : `<button class="text-btn primary" type="button" data-install-from="${escapeHtml(pair.from)}" data-install-to="${escapeHtml(pair.to)}">Télécharger</button>`}
        </div>`;
    }).join("");
  }

  async function installTranslationModel(from, to) {
    try {
      const job = await api("/api/translation/models/install", {
        method: "POST",
        body: JSON.stringify({ from, to })
      });
      translationState.installJob = job;
      els.translationProgress.classList.remove("hidden");
      els.translationProgressLabel.textContent = `Téléchargement ${languageLabel(from)} → ${languageLabel(to)}`;
      els.translationProgressValue.textContent = "0 %";
      els.translationBar.style.width = "0%";
      pollTranslationJob(job.id);
    } catch (error) {
      toast(error.message, "error");
    }
  }

  function pollTranslationJob(jobId) {
    clearTimeout(translationState.pollTimer);
    translationState.pollTimer = setTimeout(async () => {
      try {
        const job = await api(`/api/translation/models/jobs/${encodeURIComponent(jobId)}`);
        translationState.installJob = job;
        const percent = Number(job.percent || 0);
        els.translationProgress.classList.remove("hidden");
        els.translationProgressValue.textContent = `${Math.round(percent)} %`;
        els.translationBar.style.width = `${percent}%`;

        if (job.state === "running") {
          pollTranslationJob(jobId);
          return;
        }

        els.translationProgress.classList.add("hidden");
        if (job.state === "done") {
          toast(`Modèle ${languageLabel(job.from)} → ${languageLabel(job.to)} installé.`, "success");
          await loadTranslationSettings();
          await loadTranslationModels();
        } else if (job.state === "failed") {
          toast(job.error || "Installation du modèle impossible.", "error");
        }
      } catch (error) {
        toast(error.message, "error");
        els.translationProgress.classList.add("hidden");
      }
    }, 700);
  }

  async function cancelTranslationJob() {
    try {
      await api("/api/translation/models/cancel", { method: "POST" });
      els.translationProgress.classList.add("hidden");
      toast("Téléchargement annulé.");
    } catch (error) {
      toast(error.message, "error");
    }
  }

  async function removeTranslationModel(from, to) {
    if (!confirm(`Supprimer le modèle ${languageLabel(from)} → ${languageLabel(to)} ?`)) return;
    try {
      await api(`/api/translation/models?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`, { method: "DELETE" });
      toast("Modèle supprimé.", "success");
      await loadTranslationSettings();
      await loadTranslationModels();
    } catch (error) {
      toast(error.message, "error");
    }
  }

  async function clearTranslationCache() {
    if (!confirm("Vider le cache des traductions ? Les textes restent dans les conversations.")) return;
    try {
      await api("/api/translation/cache", { method: "DELETE" });
      translationState.cache = { count: 0, characters: 0 };
      renderTranslationSummary();
      toast("Cache de traduction vidé.", "success");
    } catch (error) {
      toast(error.message, "error");
    }
  }

  // ------------------------------------------------ Menu d'un message

  function closeMessageMenu() {
    if (!els.messageMenu) return;
    els.messageMenu.classList.add("hidden");
    els.messageMenu.innerHTML = "";
    els.messageMenu.dataset.messageId = "";
    state.messageMenu.messageId = null;
    document.querySelectorAll(".msg-menu-btn.open").forEach((button) => button.classList.remove("open"));
  }

  function openMessageMenu(messageId, anchor) {
    const message = findMessageById(messageId);
    if (!message || !anchor) return;
    closeMessageMenu();

    const outgoing = message.direction === "outgoing";
    const canTranslate = Boolean(messageTextToTranslate(message));
    const canEdit = outgoing && message.kind === "text";
    const alreadyTranslated = translationState.messages.has(message.id);

    const items = [];
    if (message.kind !== "call") {
      items.push('<button type="button" data-message-action="forward"><span class="menu-symbol">⇄</span>Transférer</button>');
    }
    if (canTranslate) {
      items.push(`<button type="button" data-message-action="translate"><span class="menu-symbol">🌐</span>${alreadyTranslated ? "Masquer la traduction" : "Traduire"}</button>`);
    }
    if (canEdit) {
      items.push('<button type="button" data-message-action="edit"><span class="menu-symbol">✎</span>Modifier</button>');
    }

    items.push('<div class="menu-sep"></div>');
    items.push('<div class="menu-caption">Supprimer</div>');
    items.push('<button type="button" class="danger" data-message-action="delete-me"><span class="menu-symbol">🗑</span>Supprimer pour moi</button>');
    if (outgoing) {
      items.push('<button type="button" class="danger" data-message-action="delete-all"><span class="menu-symbol">🗑</span>Supprimer pour tout le monde</button>');
    }

    els.messageMenu.dataset.messageId = message.id;
    els.messageMenu.innerHTML = items.join("");
    els.messageMenu.classList.remove("hidden");
    anchor.classList.add("open");
    state.messageMenu.messageId = message.id;

    const rect = anchor.getBoundingClientRect();
    const menuRect = els.messageMenu.getBoundingClientRect();
    let left = outgoing ? rect.right - menuRect.width : rect.left;
    left = Math.max(8, Math.min(left, window.innerWidth - menuRect.width - 8));
    let top = rect.bottom + 6;
    if (top + menuRect.height > window.innerHeight - 8) {
      top = Math.max(8, rect.top - menuRect.height - 6);
    }
    els.messageMenu.style.left = `${left}px`;
    els.messageMenu.style.top = `${top}px`;
  }

  async function handleMessageMenuAction(action, messageId) {
    if (!action || !messageId) return;
    const message = findMessageById(messageId);
    if (!message) return;

    if (action === "forward") {
      openForward(messageId);
      return;
    }

    if (action === "translate") {
      toggleMessageTranslation(messageId);
      return;
    }

    if (action === "edit") {
      openRename({
        title: "Modifier le message",
        text: "Le message sera marqué comme modifié, comme dans WhatsApp.",
        value: message.text || "",
        onSave: async (text) => {
          await api(`/api/messages/${encodeURIComponent(messageId)}`, {
            method: "PUT",
            body: JSON.stringify({ text })
          });
          await loadMessages(state.activePeerId);
          await loadConversations();
          toast("Message modifié.", "success");
        }
      });
      return;
    }

    if (action === "delete-me" || action === "delete-all") {
      const everyone = action === "delete-all";
      const question = everyone
        ? "Supprimer ce message pour tout le monde ?"
        : "Supprimer ce message pour vous uniquement ?";
      if (!confirm(question)) return;

      try {
        await api(`/api/messages/${encodeURIComponent(messageId)}?forEveryone=${everyone ? "1" : "0"}`, {
          method: "DELETE"
        });
        await Promise.all([loadMessages(state.activePeerId), loadConversations()]);
        toast(everyone ? "Message supprimé pour tout le monde." : "Message supprimé pour vous.", "success");
      } catch (error) {
        toast(error.message, "error");
      }
    }
  }

  // =============================================================== Thème

  const THEME_KEY = "whatsup.theme.v1";

  function applyTheme(theme) {
    const value = theme === "light" ? "light" : "dark";
    document.body.classList.toggle("light", value === "light");
    try { localStorage.setItem(THEME_KEY, value); } catch { /* stockage indisponible */ }
    if (els.themeSwitch) {
      els.themeSwitch.querySelectorAll("[data-theme]").forEach((button) => {
        button.classList.toggle("active", button.dataset.theme === value);
      });
    }
  }

  function toggleTheme() {
    applyTheme(document.body.classList.contains("light") ? "dark" : "light");
  }

  // =============================================================== Profil

  function openProfile() {
    if (!state.me) return;
    els.profileName.textContent = state.me.displayName;
    setAvatar(els.profileAvatar, "me", state.me.avatarVersion, true);
    els.profileModal.classList.remove("hidden");
    applyTheme(document.body.classList.contains("light") ? "light" : "dark");
    loadTranslationSettings();
  }

  function closeProfile() {
    els.profileModal.classList.add("hidden");
  }

  function openStandaloneImage(url, title) {
    viewerMessage = null;
    els.viewerImage.src = url;
    els.viewerTitle.textContent = title || "Photo";
    els.viewerTime.textContent = "";
    els.viewerDownload.classList.add("hidden");
    els.imageViewer.classList.remove("hidden");
  }

  function openPeerAvatarViewer(peerId, name, version) {
    if (!version) return;
    openStandaloneImage(`/api/peers/${encodeURIComponent(peerId)}/avatar?v=${encodeURIComponent(version)}`, name || "Photo de profil");
  }

  // ============================================================ Transfert

  function forwardCandidates() {
    const list = [];
    for (const conversation of state.conversations) {
      list.push({ peerId: conversation.peerId, name: conversation.name, avatarVersion: conversation.avatarVersion });
    }
    for (const peer of state.peers) {
      if (list.some((item) => item.peerId === peer.peerId)) continue;
      list.push({ peerId: peer.peerId, name: peer.name, avatarVersion: peer.avatarVersion });
    }
    return list;
  }

  function openForward(messageId) {
    if (!messageId) return;
    state.forward.messageId = messageId;
    state.forward.targets = new Set();
    renderForwardList();
    els.forwardModal.classList.remove("hidden");
  }

  function renderForwardList() {
    const candidates = forwardCandidates();
    els.forwardList.innerHTML = candidates.length === 0
      ? '<p class="empty-hint">Aucune discussion disponible.</p>'
      : candidates.map((item) => `
        <button class="forward-row${state.forward.targets.has(item.peerId) ? " selected" : ""}" type="button" data-forward-target="${escapeHtml(item.peerId)}">
          <span class="avatar small">${avatarInner(item.peerId, item.avatarVersion, item.peerId === state.me?.peerId)}</span>
          <span>${escapeHtml(item.name)}</span>
          <span class="forward-check"></span>
        </button>`).join("");
  }

  function closeForward() {
    els.forwardModal.classList.add("hidden");
    state.forward.messageId = null;
    state.forward.targets = new Set();
  }

  async function submitForward() {
    const messageId = state.forward.messageId;
    const targets = Array.from(state.forward.targets);
    if (!messageId || targets.length === 0) {
      toast("Choisissez au moins une discussion.", "error");
      return;
    }

    closeForward();
    let done = 0;
    for (const peerId of targets) {
      try {
        await api(`/api/peers/${encodeURIComponent(peerId)}/forward`, {
          method: "POST",
          body: JSON.stringify({ messageIds: [messageId] })
        });
        done += 1;
      } catch (error) {
        toast(error.message, "error");
      }
    }

    await loadConversations();
    if (state.activePeerId && targets.includes(state.activePeerId)) await loadMessages(state.activePeerId);
    if (done > 0) toast(`Message transféré vers ${done} discussion${done > 1 ? "s" : ""}.`, "success");
  }

  async function votePoll(messageId, optionId) {
    try {
      await api(`/api/messages/${encodeURIComponent(messageId)}/vote`, {
        method: "POST",
        body: JSON.stringify({ optionId })
      });
      if (state.activePeerId) await loadMessages(state.activePeerId);
    } catch (error) {
      toast(error.message, "error");
    }
  }

  // =========================================== Médias, liens et documents

  async function openShared() {
    if (!state.activePeerId) return;
    els.sharedModal.classList.remove("hidden");
    els.sharedBody.innerHTML = '<p class="shared-empty">Chargement…</p>';
    try {
      state.shared.data = await api(`/api/peers/${encodeURIComponent(state.activePeerId)}/shared`);
      renderShared();
    } catch (error) {
      els.sharedBody.innerHTML = `<p class="shared-empty">${escapeHtml(error.message)}</p>`;
    }
  }

  function setSharedTab(tab) {
    state.shared.tab = tab;
    els.sharedTabs.querySelectorAll("[data-shared]").forEach((chip) => {
      chip.classList.toggle("active", chip.dataset.shared === tab);
    });
    renderShared();
  }

  function renderShared() {
    const data = state.shared.data || {};
    const tab = state.shared.tab;

    if (tab === "media") {
      const media = data.media || [];
      els.sharedBody.innerHTML = media.length === 0
        ? '<p class="shared-empty">Aucune photo ni vidéo partagée.</p>'
        : `<div class="shared-grid">${media.map((item) => item.kind === "video"
            ? `<button type="button" data-shared-media="${escapeHtml(item.id)}"><video src="/api/files/${encodeURIComponent(item.fileId)}" preload="metadata" muted></video></button>`
            : `<button type="button" data-shared-media="${escapeHtml(item.id)}"><img src="/api/files/${encodeURIComponent(item.fileId)}" alt="" loading="lazy"></button>`).join("")}</div>`;
      return;
    }

    if (tab === "documents") {
      const documents = data.documents || [];
      const names = (data.media || []).length;
      els.sharedBody.innerHTML = documents.length === 0
        ? `<p class="shared-empty">Aucun document partagé${names ? "" : ""}.</p>`
        : documents.map((item) => {
            const [label, variant] = fileTypeLabel(item.fileName, item.mimeType);
            return `<div class="shared-row" data-shared-doc="${escapeHtml(item.id)}" role="button" tabindex="0" title="Ouvrir le document">
              <span class="file-type-icon ${variant}">${escapeHtml(label)}</span>
              <span class="shared-name" title="${escapeHtml(item.fileName)}">${escapeHtml(item.fileName)}</span>
              <span class="file-size">${escapeHtml(formatBytes(item.fileSize))}</span>
            </div>`;
          }).join("");
      return;
    }

    const links = data.links || [];
    els.sharedBody.innerHTML = links.length === 0
      ? '<p class="shared-empty">Aucun lien partagé.</p>'
      : links.map((item) => `<div class="shared-row">
          <a href="${escapeHtml(item.url)}" data-external-link="${escapeHtml(item.url)}">${escapeHtml(item.url)}</a>
          <span class="at">${escapeHtml(formatRelative(item.createdAt))}</span>
        </div>`).join("");
  }

  function findSharedItem(itemId) {
    const data = state.shared.data || {};
    return (data.media || []).find((item) => item.id === itemId)
      || (data.documents || []).find((item) => item.id === itemId)
      || null;
  }

  function openSharedMedia(messageId) {
    const item = findSharedItem(messageId);
    if (!item) return;
    if (item.kind === "video") openMessageFile(item);
    else openImageViewer(item);
  }

  // ==================================== Effacer / supprimer la discussion

  async function clearConversation() {
    const peer = activePeer();
    if (!peer) return;
    closeContactMenu();
    if (!window.confirm("Effacer tous les messages de cette discussion sur ce poste ?")) return;

    try {
      await api(`/api/peers/${encodeURIComponent(peer.peerId)}/messages`, { method: "DELETE" });
      await Promise.all([loadMessages(peer.peerId), loadConversations()]);
      toast("Discussion effacée sur ce poste.", "success");
    } catch (error) {
      toast(error.message, "error");
    }
  }

  async function deleteConversation() {
    const peer = activePeer();
    if (!peer) return;
    closeContactMenu();
    if (!window.confirm(`Supprimer la discussion avec ${peer.name} ? Les messages, fichiers et appels seront effacés.`)) return;

    try {
      await api(`/api/peers/${encodeURIComponent(peer.peerId)}`, { method: "DELETE" });
      state.activePeerId = null;
      els.app.classList.remove("chat-open");
      updateMain();
      await Promise.all([loadPeers(), loadConversations(), loadCalls()]);
      toast("Discussion supprimée.", "success");
    } catch (error) {
      toast(error.message, "error");
    }
  }

  // ==================================================== Historique d'appels

  async function deleteCall(callId) {
    if (!callId) return;
    try {
      await api(`/api/calls/${encodeURIComponent(callId)}`, { method: "DELETE" });
      await loadCalls();
    } catch (error) {
      toast(error.message, "error");
    }
  }

  async function clearCalls() {
    if (!window.confirm("Effacer tout l'historique des appels ?")) return;
    try {
      await api("/api/calls", { method: "DELETE" });
      await loadCalls();
      toast("Historique des appels effacé.", "success");
    } catch (error) {
      toast(error.message, "error");
    }
  }

  // ======================================== Pièce jointe, caméra et sondage

  function updateViewOnceToggle() {
    if (!els.viewOnceToggle) return;
    els.viewOnceToggle.classList.toggle("active", state.viewOnce);
    els.viewOnceToggle.setAttribute("aria-pressed", state.viewOnce ? "true" : "false");
    if (els.viewOnceSwitch) els.viewOnceSwitch.textContent = state.viewOnce ? "Activée" : "Désactivée";
  }

  function closeViewOnceViewer() {
    if (!els.viewOnceViewer) return;
    els.viewOnceStage.innerHTML = "";
    els.viewOnceViewer.classList.add("hidden");
    renderMessages();
  }

  // Le média est marqué comme consulté avant d'être affiché : impossible de le revoir.
  async function openViewOnce(messageId) {
    const message = findMessageById(messageId);
    if (!message || !message.isViewOnce) return;
    if (message.viewOnceOpened) {
      toast("Ce média a déjà été consulté.", "error");
      return;
    }
    if (!message.fileId) return;

    try {
      await api(`/api/messages/${encodeURIComponent(messageId)}/viewed`, { method: "POST" });
      message.viewOnceOpened = true;
    } catch (error) {
      toast(error.message, "error");
      return;
    }

    renderMessages();
    const url = `/api/files/${encodeURIComponent(message.fileId)}`;
    els.viewOnceStage.innerHTML = message.kind === "image"
      ? `<img src="${url}" alt="Média en vue unique">`
      : message.kind === "video"
        ? `<video src="${url}" controls autoplay playsinline></video>`
        : `<audio src="${url}" controls autoplay></audio>`;
    els.viewOnceViewer.classList.remove("hidden");
  }

  function toggleAttachMenu(force) {
    const open = force === undefined ? els.attachMenu.classList.contains("hidden") : force;
    els.attachMenu.classList.toggle("hidden", !open);
  }

  async function openCamera() {
    if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
      toast("La caméra n'est pas disponible sur ce poste.", "error");
      return;
    }

    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        audio: false,
        video: { facingMode: state.camera.facing, width: { ideal: 1280 } }
      });
      state.camera.stream = stream;
      els.cameraVideo.srcObject = stream;
      els.cameraModal.classList.remove("hidden");
    } catch {
      toast("Caméra inaccessible. Vérifiez qu'aucune autre application ne l'utilise.", "error");
    }
  }

  function closeCamera() {
    if (state.camera.stream) {
      state.camera.stream.getTracks().forEach((track) => track.stop());
      state.camera.stream = null;
    }
    els.cameraVideo.srcObject = null;
    els.cameraModal.classList.add("hidden");
  }

  async function switchCamera() {
    state.camera.facing = state.camera.facing === "environment" ? "user" : "environment";
    closeCamera();
    await openCamera();
  }

  function capturePhoto() {
    const video = els.cameraVideo;
    if (!video.videoWidth) {
      toast("La caméra n'est pas encore prête.", "error");
      return;
    }

    const canvas = document.createElement("canvas");
    canvas.width = video.videoWidth;
    canvas.height = video.videoHeight;
    canvas.getContext("2d").drawImage(video, 0, 0, canvas.width, canvas.height);

    canvas.toBlob((blob) => {
      if (!blob) return;
      const file = new File([blob], `photo-${Date.now()}.png`, { type: "image/png" });
      closeCamera();
      uploadFile(file, "image");
    }, "image/png", 0.92);
  }

  function openPollComposer() {
    state.pollDraft = ["", ""];
    els.pollQuestion.value = "";
    renderPollDraft();
    els.pollModal.classList.remove("hidden");
    els.pollQuestion.focus();
  }

  function readPollDraft() {
    els.pollOptionsEdit.querySelectorAll("[data-poll-option-input]").forEach((input) => {
      state.pollDraft[Number(input.dataset.pollOptionInput)] = input.value;
    });
  }

  function renderPollDraft(focusIndex = -1) {
    els.pollOptionsEdit.innerHTML = state.pollDraft.map((value, index) =>
      `<input type="text" data-poll-option-input="${index}" maxlength="120" placeholder="Option ${index + 1}" value="${escapeHtml(value)}">`).join("");
    if (focusIndex >= 0) {
      const inputs = els.pollOptionsEdit.querySelectorAll("[data-poll-option-input]");
      const target = inputs[focusIndex];
      if (target) target.focus();
    }
  }

  async function sendPoll() {
    readPollDraft();
    const question = els.pollQuestion.value.trim();
    const options = state.pollDraft.map((value) => value.trim()).filter((value) => value.length > 0);
    if (question.length === 0) {
      toast("Tapez la question du sondage.", "error");
      return;
    }
    if (options.length < 2) {
      toast("Ajoutez au moins deux options.", "error");
      return;
    }
    if (!state.activePeerId) return;

    els.pollSend.disabled = true;
    try {
      await api(`/api/peers/${encodeURIComponent(state.activePeerId)}/poll`, {
        method: "POST",
        body: JSON.stringify({ question, options })
      });
      els.pollModal.classList.add("hidden");
      await Promise.all([loadMessages(state.activePeerId), loadConversations()]);
      toast("Sondage envoyé.", "success");
    } catch (error) {
      toast(error.message, "error");
    } finally {
      els.pollSend.disabled = false;
    }
  }

  // ==================================================== Statuts : j'aime

  function isStatusLiked(statusId) {
    return statusState.liked.has(statusId);
  }

  function saveStatusLikes() {
    try { localStorage.setItem("whatsup.likes.v1", JSON.stringify(Array.from(statusState.liked))); } catch { /* ignoré */ }
  }

  async function toggleStatusLike() {
    const viewer = statusState.viewer;
    const item = viewer.items[viewer.index];
    if (!item) return;
    if (viewer.mine) {
      toast("Vous ne pouvez pas aimer votre propre statut.");
      return;
    }

    const liked = !isStatusLiked(item.id);
    if (liked) statusState.liked.add(item.id);
    else statusState.liked.delete(item.id);
    saveStatusLikes();
    renderStatusViewer();

    try {
      if (viewer.mine) {
        await api(`/api/statuses/${encodeURIComponent(item.id)}/like`, {
          method: "POST",
          body: JSON.stringify({ liked })
        });
      } else {
        await api(`/api/statuses/peer/${encodeURIComponent(viewer.owner.peerId)}/${encodeURIComponent(item.id)}/like`, {
          method: "POST",
          body: JSON.stringify({ liked })
        });
      }
      await loadStatuses();
      refreshViewerItems();
    } catch (error) {
      toast(error.message, "error");
    }
  }

  async function sendStatusComment() {
    const viewer = statusState.viewer;
    const item = viewer.items[viewer.index];
    const text = els.statusCommentInput.value.trim();
    if (!item || !text) return;
    if (viewer.mine) {
      toast("Vous ne pouvez pas commenter votre propre statut.");
      return;
    }

    els.statusCommentInput.value = "";
    try {
      if (viewer.mine) {
        await api(`/api/statuses/${encodeURIComponent(item.id)}/comment`, {
          method: "POST",
          body: JSON.stringify({ text })
        });
      } else {
        await api(`/api/statuses/peer/${encodeURIComponent(viewer.owner.peerId)}/${encodeURIComponent(item.id)}/comment`, {
          method: "POST",
          body: JSON.stringify({ text })
        });
      }
      toast("Commentaire envoyé.", "success");
      await loadStatuses();
      refreshViewerItems();
    } catch (error) {
      toast(error.message, "error");
    }
  }

  async function toggleStatusPaused() {
    const viewer = statusState.viewer;
    const item = viewer.items[viewer.index];
    if (!item || !viewer.mine) return;

    try {
      await api(`/api/statuses/${encodeURIComponent(item.id)}/pause?paused=${item.paused ? "false" : "true"}`, { method: "POST" });
      await loadStatuses();
      const refreshed = statusState.mine.find((entry) => entry.id === item.id);
      viewer.items = statusState.mine.slice();
      const index = viewer.items.findIndex((entry) => entry.id === item.id);
      viewer.index = index >= 0 ? index : 0;
      renderStatusViewer();
      toast(refreshed && refreshed.paused ? "Statut en pause." : "Statut de nouveau partagé.", "success");
    } catch (error) {
      toast(error.message, "error");
    }
  }

  function renderPrivacyPeers() {
    const value = els.statusPrivacy.value;
    const needsList = value === "except" || value === "only";
    els.statusPrivacyPeers.classList.toggle("hidden", !needsList);
    if (!needsList) return;

    const peers = state.peers.filter((peer) => peer.peerId !== state.me?.peerId);
    els.statusPrivacyPeers.innerHTML = peers.length === 0
      ? '<span class="empty-hint">Aucun contact pour le moment.</span>'
      : peers.map((peer) => `<label><input type="checkbox" data-privacy-peer="${escapeHtml(peer.peerId)}"${statusState.privacyPeers.has(peer.peerId) ? " checked" : ""}> ${escapeHtml(peer.name)}</label>`).join("");
  }

  // ------------------------------------------------------------------ Init

  async function init() {
    applyTheme(localStorage.getItem(THEME_KEY) || "dark");
    bindEvents();
    setupDesktop();

    // Les notifications de bureau (clic sur la pastille) ouvrent la discussion.
    window.__whatsUpOpenConversation = (peerId) => {
      if (peerId) openConversation(peerId);
    };

    try {
      await loadMe();
      await Promise.all([loadPeers(), loadConversations(), loadCalls(), loadBlocks()]);
      await loadStatuses();
    } catch {
      toast("Impossible de joindre le serveur local.", "error");
    }

    // Le moteur Argos est détecté en arrière-plan : l'interface n'attend pas Python.
    loadTranslationSettings();

    connectSocket();
    autosize();
    setInterval(pruneTyping, 2000);
    setInterval(loadPeers, 4000);
    setInterval(loadConversations, 8000);
    setInterval(loadStatuses, 20000);
    setInterval(() => { if (state.view === "calls") loadCalls(); }, 10000);
    setInterval(() => {
      if (state.activePeerId && !state.socketOpen) loadMessages(state.activePeerId);
    }, 5000);
    window.addEventListener("beforeunload", () => {
      if (call.callId) sendCallSignal("end");
      stopRingtone();
    });
  }

  document.addEventListener("DOMContentLoaded", init);
})();
