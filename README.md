# WhatsUP Desktop — messagerie locale Windows

Application de messagerie de bureau pour Windows, développée en **C# / .NET 8**. Le projet propose une interface inspirée de WhatsApp Desktop et permet à plusieurs ordinateurs d'un même réseau local de communiquer sans serveur central.

> Nom de l'interface : **vibeChat**. Les noms historiques de la solution et du projet (`CaramelChat.sln`, `CaramelChat/`) sont conservés dans le code.

## Fonctionnalités

- Découverte automatique des postes du réseau local par UDP broadcast.
- Discussions en temps réel via WebSocket et échanges HTTP directs entre postes.
- Messages texte, photos, vidéos, messages vocaux et fichiers.
- Historique local avec SQLite et file d'attente pour les messages en attente.
- Appels audio/vidéo pair-à-pair avec WebRTC.
- Contacts, profils, statuts, blocage et historique des appels.
- Interface Windows WPF intégrant l'interface web avec WebView2.

Les fonctionnalités ci-dessus sont décrites dans le code et la documentation du projet ; les tests automatisés disponibles couvrent notamment l'identité locale, le registre des pairs, la persistance et la découverte UDP.

## Technologies

| Domaine | Technologie |
|---|---|
| Langage | C# 12 |
| Plateforme | .NET 8 / ASP.NET Core |
| Application de bureau | WPF + WebView2 |
| Serveur local | Kestrel / Minimal API |
| Communication temps réel | WebSocket |
| Découverte réseau | UDP broadcast |
| Appels audio/vidéo | WebRTC |
| Persistance | SQLite |
| Interface | HTML, CSS, JavaScript |

## Architecture du dépôt

```text
WhatsUP-Desktop/
├── CaramelChat.sln
├── CaramelChat/
│   ├── Data/             # Accès à la base SQLite
│   ├── Desktop/          # Fenêtre Windows WPF
│   ├── Endpoints/        # Routes HTTP de l'application
│   ├── Models/           # Modèles de données
│   ├── Services/         # Réseau, messages, statuts et appels
│   ├── wwwroot/          # Interface HTML/CSS/JavaScript
│   ├── tools/            # Outils auxiliaires
│   └── CaramelChat.csproj
├── tests/
│   └── CaramelChat.Tests/
├── PLAN.md
├── README.md
└── .gitignore
```

## Prérequis

- Windows 10 ou Windows 11 (64 bits).
- SDK .NET 8.
- Runtime WebView2.
- Visual Studio 2022 (facultatif) avec les composants de développement .NET Desktop et ASP.NET.
- Pour la découverte automatique, les ordinateurs doivent être sur le même réseau local et les ports nécessaires doivent être autorisés par le pare-feu.

## Installation et lancement

1. Cloner le dépôt :
   ```bash
   git clone <URL_DU_DEPOT>
   cd WhatsUP-Desktop
   ```
2. Restaurer les dépendances :
   ```bash
   dotnet restore CaramelChat.sln
   ```
3. Lancer l'application :
   ```bash
   dotnet run --project CaramelChat/CaramelChat.csproj
   ```

Ou ouvrir `CaramelChat.sln` dans Visual Studio 2022 et lancer le projet de bureau.

## Tests

Le projet contient un programme de tests sans framework externe :

```bash
 dotnet run --project tests/CaramelChat.Tests/CaramelChat.Tests.csproj
```

Exécuter les tests sur Windows avec le SDK .NET 8 installé.

## Configuration et données locales

Les ports et limites de transfert sont configurés dans `CaramelChat/appsettings.json` : HTTP `5080`, HTTPS `5443`, découverte UDP `50001` et taille maximale par défaut `64 Mo`.

Les données d'exécution (base SQLite, avatars, fichiers reçus et certificat local) sont stockées dans `CaramelChat/App_Data/`. Ce dossier est volontairement exclu de Git : il peut contenir des données personnelles et des éléments propres à chaque ordinateur. Il est créé au premier lancement.

## Documentation complémentaire

- [`PLAN.md`](PLAN.md) : objectifs, fonctionnalités, architecture, modèle de données et pistes d'évolution.

## État du projet

Le plan de travail décrit les fonctionnalités considérées comme livrées et les évolutions envisagées. Consultez `PLAN.md` pour le détail. Les fonctionnalités non encore implémentées ne doivent pas être présentées comme disponibles.

## Contribution

Les améliorations peuvent être proposées sous forme de branches et de pull requests. Privilégier des changements ciblés, des messages de commit explicites et l'exécution des tests avant chaque proposition.

## Licence

pour cela
