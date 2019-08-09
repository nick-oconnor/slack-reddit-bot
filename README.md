# Slack Reddit Bot
A slack app for replying to specific messages with images from reddit. An example slack app using [r/birdwitharms](https://www.reddit.com/r/birdswitharms/ "Birds With Arms Subreddit") can be installed by [clicking here](https://birdswitharms.xphysics.net/install "Install the Birds With Arms Slack App").

## Requirements
* Docker host for running linux containers
* MySQL instance

## Builds ![Teamcity Build Status](https://teamcity.xphysics.net/app/rest/builds/buildType:(id:SlackRedditBot_BuildDeploy)/statusIcon "Teamcity Build Status")
Docker images (for Linux) are available on [Docker Hub](https://cloud.docker.com/repository/docker/ngpitt/slack-reddit-bot "Slack Reddit Bot Docker Repo").

## Usage
1. Create an app on Slack
2. Create a database for the app with the following schema:

    ```
    CREATE TABLE `instances` (
      `team_id` char(9) NOT NULL,
      `access_token` char(76) NOT NULL,
      CONSTRAINT `instances_pk` PRIMARY KEY (`team_id`)
    ) ENGINE=InnoDB DEFAULT CHARSET=utf8;
    ```
3. Create a config file named `appsettings.Production.json` using the following template:

    ```
    {
      "ConnectionStrings": {
        "AppDbContext": "Server=myServerAddress;Database=myDatabase;Uid=myUsername;Pwd=myPassword"
      },
      "AppSettings": {
        "AppId": "slack app id",
        "ClientId": "slack app client id",
        "ClientSecret": "slack app client secret",
        "SigningSecret": "unique string for signing auth requests",
        "Scopes": "channels:history,chat:write:bot",
        "DisplayName": "app display name",
        "ProductName": "app name (no spaces or special chars)",
        "Subreddit": "subreddit to use (excluding leading r/)",
        "ImageExtensions": ["jpg", "png", "gif"],
        "Triggers": ["array", "of", "trigger", "words"]
      }
    }
    ```
4. Run the app using the following command (a proxy should be used for TLS support):

    ```
    docker run -dit --restart always --name app-name -p 80:80 -v /path/to/appsettings.Production.json:/app/appsettings.Production.json ngpitt/slack-reddit-bot:desiredCommitHash
    ```
