# Plan de travail — What’sUP

Document d'organisation du projet : objectif, fonctionnalités, outils, découpage en phases,
modèle de données et évolutions. La version livrée correspond aux phases 0 à 8.

---

## 1. Objectif

Reproduire une messagerie type **WhatsApp Desktop** fonctionnant **sur un réseau local**, avec :

- une **application de bureau Windows écrite en C#** (WPF + WebView2) ;
- la **découverte automatique** des autres postes du réseau (nom de machine + adresse IP) ;
- des échanges directs **par adresse IP** entre postes ;
- un historique **SQLite** ;
- l'envoi de **textes, messages vocaux, images, vidéos et fichiers** (taille maximale définie) ;
- des **statuts** texte sur fond de couleur, photo et vidéo, visibles sur le réseau ;
- le **blocage** d'un contact et le masquage des statuts, réversibles ;
- une interface **WhatsApp Desktop sombre** (logo, motif « doodle », onglets Discussions / Appels / Statuts / Contacts).

---

## 2. Fonctionnalités

### 2.1 Version 1 (livrée)

- [x] Découverte automatique des postes : annonce UDP broadcast toutes les 3 s + sonde de démarrage
- [x] Affichage : nom du poste (ou nom modifié) + **adresse IP en dessous** + port
- [x] Modification de son propre nom (annoncée aux autres postes)
- [x] Renommage local d'un contact (surnom visible seulement sur ce PC)
- [x] **Messages à moi-même** : discussion avec son propre poste (texte, photos, vidéos,
      vocaux, fichiers), stockée localement et toujours proposée en tête des contacts
- [x] Liste des discussions avec dernier message, heure et compteur de non-lus
- [x] Messages texte en temps réel (WebSocket) + repli par interrogation périodique
- [x] Messages vocaux (MediaRecorder → WebM/Opus) avec durée
- [x] Images (aperçu intégré, ouverture en grand)
- [x] Fichiers (carte avec nom + taille + téléchargement)
- [x] Vidéos (lecture dans la bulle, glisser-déposer, envoi par le bouton caméra)
- [x] Limite de taille configurable (64 Mo par défaut), vérifiée côté navigateur **et** serveur
- [x] Historique SQLite, fichiers stockés localement
- [x] Statut d'envoi (✓ / ✓✓ / ⏱ en attente)
- [x] **File d'attente et renvoi automatique** : un message écrit quand l'autre poste est
      éteint part dès qu'il se connecte (OutboxService, identifiants stables sans doublon)
- [x] **Appels audio et vidéo** pair-à-pair (WebRTC) avec micro, webcam, muet, caméra, raccrocher
- [x] **Photos de profil** modifiables, annoncées en UDP et mises en cache chez les autres postes
- [x] **Indicateur « écrit… »** dans la discussion et la liste des discussions
- [x] **Annulation d'un envoi** jamais distribué (statut `canceled`, accordéon « Annuler l'envoi »)
- [x] **Historique des appels** (Sortant / Entrant / Manqué, durée, rappel en un clic)
- [x] **Statuts** texte (fond de couleur), photo et vidéo avec légende, expiration 24 h,
      vues par contact, visualiseur avec barres de progression
- [x] **Blocage d'un contact** (messages et appels refusés) et **masquage des statuts**,
      déblocage depuis la discussion ou la liste des bloqués
- [x] **HTTPS avec certificat auto-signé** généré au démarrage : micro, vocaux et appels
      depuis les autres PC
- [x] Sonnerie d'appel entrant et tonalité de retour générées (Web Audio)
- [x] Thème WhatsApp sombre, logo « What’sUP », motif de fond, colonne d'onglets
- [x] Interface responsive (mobile : une colonne)

### 2.2 Version 2 (à définir ensemble)

- [ ] Accusés de lecture réels entre postes (double coche bleue)
- [ ] Chiffrement de bout en bout
- [ ] Suppression / modification de message (les statuts peuvent être supprimés par l'API)
- [ ] Discussions de groupe
- [ ] Notifications système et son
- [ ] Transfert de fichiers en plusieurs morceaux avec reprise
- [ ] Appels de groupe, partage d'écran, relais TURN
- [ ] Recherche plein texte dans l'historique
- [ ] Export de conversation

---

## 3. Outils et technologies

| Domaine | Outil / technologie | Pourquoi |
|---|---|---|
| Langage | **C# 12** | Demande explicite du projet |
| Framework web | **ASP.NET Core 8 (Minimal API)** | Serveur HTTP + API sans surcharge MVC |
| Serveur | **Kestrel** | Intégré, multi-plateforme, écoute sur `0.0.0.0` |
| Base de données | **SQLite** via `Microsoft.Data.Sqlite` | Fichier local, zéro installation, adapté au LAN |
| Découverte réseau | **UDP broadcast** (`UdpClient`, `System.Net.NetworkInformation`) | Trouver les postes sans serveur central |
| Échanges | **HTTP/JSON + multipart** (`HttpClient`, `IHttpClientFactory`) | Simple, direct par IP, pas de broker |
| Temps réel | **WebSocket natif** (`System.Net.WebSockets`) | Aucune dépendance JS (SignalR non nécessaire) |
| Interface | **HTML + CSS + JavaScript vanilla dans WebView2** | Même interface que la version web, dans une fenêtre native |
| Enregistrement vocal | **MediaRecorder API** | Vocal sans plugin ni logiciel tiers |
| Appels audio/vidéo | **WebRTC** (`RTCPeerConnection`, `getUserMedia`) + signalisation HTTP | Son et image directs entre navigateurs, sans serveur de médias |
| File d'attente | **BackgroundService** (`OutboxService`) | Reprise automatique des envois, sans dépendance externe |
| IDE | **Visual Studio 2022** (ou VS Code) | Débogage, profils de lancement |
| SDK | **.NET 8 SDK** | LTS, compatible VS 2022 17.8+ |
| Tests manuels | `curl`, navigateur, deux PC du réseau | Vérification rapide des routes et du transfert |

---

## 4. Architecture

```
Navigateur (localhost:5080)
   │  fetch /api/*          WebSocket /ws
   ▼
ASP.NET Core (Kestrel, C#)
   ├── Endpoints/ChatEndpoints.cs  → API locale
   ├── Services/ChatService.cs     → enregistre + transfère vers le poste distant
   ├── Services/DiscoveryService.cs→ UDP 50001 (annonces + sondes + version d'avatar)
   ├── Services/OutboxService.cs   → renvoi périodique des messages en attente
   ├── Services/AvatarStore.cs     → photos de profil (locale + cache)
   ├── Services/PeerRegistry.cs    → pairs vus récemment (mémoire)
   ├── Services/LocalIdentity.cs   → PeerId stable + nom + IP locale
   ├── Services/StatusService.cs   → statuts, fil réseau, vues, expiration 24 h
   ├── Services/CertificateFactory.cs → certificat auto-signé HTTPS
   ├── Services/RealtimeHub.cs     → diffusion aux onglets ouverts
   └── Data/AppDatabase.cs         → SQLite App_Data/caramelchat.db
                                    fichiers App_Data/files/
```

Principe : **pas de serveur central**. Chaque poste est à la fois client et serveur ; les
messages sont poussés en HTTP vers le poste destinataire, qui les stocke dans sa propre base.
La découverte se fait en UDP broadcast sur le même sous-réseau.

---

## 5. Modèle de données

```sql
Settings (Key TEXT PK, Value TEXT)

Contacts (
  PeerId TEXT PK, RemoteName TEXT, MachineName TEXT,
  Ip TEXT, Port INTEGER, Alias TEXT NULL, LastSeen TEXT NULL,
  AvatarVersion TEXT NULL, AvatarFile TEXT NULL
)

Messages (
  Id TEXT PK, PeerId TEXT, Direction TEXT, Kind TEXT,
  Text TEXT NULL, FileId TEXT NULL, FileName TEXT NULL, MimeType TEXT NULL,
  FileSize INTEGER, DurationMs INTEGER, Status TEXT, IsRead INTEGER, CreatedAt TEXT
)
CREATE INDEX IX_Messages_Peer_Created ON Messages (PeerId, CreatedAt);
```

- `Kind` : `text`, `image`, `video`, `audio`, `file`
- `Direction` : `outgoing` (envoyé), `incoming` (reçu)
- `Status` : `sending`, `queued` (en attente de connexion), `delivered`, `failed`, `canceled`
- Statuts : `Statuses` (texte/fond, photo/vidéo, expiration 24 h), `StatusViews` (vues),
  `Blocks` (`BlockChat`, `BlockStatus`)
- Les colonnes d'avatar sont ajoutées par migration douce si la base existe déjà.
- `CreatedAt` : ISO 8601 UTC (tri lexicographique fiable)

---

## 6. Découpage en phases

| Phase | Contenu | Statut |
|---|---|---|
| 0. Cadrage | Choix de la stack (ASP.NET Core + SQLite + UDP), définition des fonctionnalités et des ports | ✅ |
| 1. Socle | Projet, Kestrel sur `0.0.0.0:5080`, page statique, SQLite et schéma | ✅ |
| 2. Identité & découverte | `PeerId` persistant, nom modifiable, annonces UDP, registre des pairs, API `/api/peers` | ✅ |
| 3. Messagerie | Envoi/réception de textes, historique, conversations, non-lus, WebSocket | ✅ |
| 4. Médias | Upload images / vocaux / fichiers, limite de taille, stockage et téléchargement | ✅ |
| 5. Interface & finitions | Thème caramel/crème/beige, WhatsApp Desktop, renommage, responsive, documentation | ✅ |
| 6. Temps réel avancé | File d'attente et reprise, appels audio/vidéo WebRTC, photos de profil, indicateur « écrit… » | ✅ |
| 7. Statuts & blocage | Statuts texte/photo/vidéo, vues, blocage réversible, HTTPS auto-signé, sonnerie | ✅ |
| 8. Version bureau | Fenêtre WPF + WebView2, pont fichiers natif, cartes de fichiers Ouvrir / Enregistrer sous, visualiseur d’image, recherche dans une discussion | ✅ |
| 9. Durcissement (v2) | Accusés de lecture, groupes, notifications | ⏳ |

---

## 7. Configuration

- Ports : HTTP **5080/TCP**, HTTPS **5443/TCP** (certificat auto-signé), découverte
  **50001/UDP** (modifiables dans `appsettings.json`).
- Taille maximale : `Chat:MaxFileSizeMb` = **64** (contrôlée côté client et serveur ; Kestrel
  autorise `MaxFileSizeMb + 10 Mo` pour les en-têtes multipart).
- Profils Visual Studio : « CaramelChat (reseau local) » (0.0.0.0:5080) et
  « CaramelChat (localhost seulement) ».
- Données : `CaramelChat/App_Data/` (créé au premier lancement, ignoré par Git).

---

## 8. Critères d'acceptation

1. Deux PC du même réseau, application lancée des deux côtés → chaque poste voit l'autre dans
   « Sur le réseau » en moins de 10 secondes, avec le nom de machine et l'IP.
2. Le nom affiché peut être modifié et se met à jour chez l'autre poste.
3. Un texte envoyé arrive chez l'autre poste et s'affiche dans la conversation (historique
   conservé après redémarrage de l'application).
4. Une image s'affiche dans la bulle, un vocal se lit avec son lecteur, un fichier se
   télécharge avec son nom d'origine.
5. Un fichier au-delà de la limite est refusé avec un message clair.
6. Si le poste destinataire est éteint, le message est enregistré et marqué « en attente » (⏱) ;
   il part tout seul, sans doublon, dès que ce poste redémarre et se fait découvrir.
7. Deux postes en ligne peuvent s'appeler en audio et en vidéo (micro et webcam), avec
   répondre / raccrocher / muet / caméra, sur `http://localhost` ou en HTTPS.
8. Une photo de profil choisie sur un poste apparaît chez l'autre en quelques secondes.
9. Quand l'autre personne écrit, « écrit… » s'affiche dans la discussion et dans la liste.
10. Un statut texte, photo ou vidéo publié sur un poste apparaît dans l'onglet Statuts des
    autres postes, avec la liste des vues chez son auteur, et disparaît après 24 h.
11. Un contact bloqué ne peut plus écrire ni appeler, et un contact masqué ne reçoit pas les
    statuts ; les deux réglages se débloquent depuis l'application.
12. L'interface HTTPS (`https://IP:5443`) permet d'enregistrer un vocal et de passer un appel.

---

## 9. Limites connues (v1)

- Pas d'authentification ni de chiffrement : réseau de confiance uniquement.
- Les envois sont repris automatiquement, mais seul le poste expéditeur doit être allumé pour
  les conserver : un message jamais envoyé reste dans sa base jusqu'à la connexion du destinataire.
- Micro et caméra (vocaux, appels) nécessitent un contexte sécurisé : utiliser l'adresse
  HTTPS auto-signée (`https://IP:5443`) et accepter l'avertissement du navigateur.
- Appels limités au réseau local (pas de relais TURN) et aux contacts en ligne.
- Une instance par PC (identité et base partagées) ; la découverte dépend du pare-feu et du
  sous-réseau (les réseaux Wi-Fi « invité » isolés ne fonctionnent pas).
- Historique chargé par conversation : 500 derniers messages.
