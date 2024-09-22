using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Dapper;
using Microsoft.Data.Sqlite;
using System.Data;
using SameSiteMode = Microsoft.AspNetCore.Http.SameSiteMode;
using Newtonsoft.Json.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.WebEncoders.Testing;

string steamApiKey = "PutSteamKeyHerePlease";
string API_URL = "ApiUrlGoesHerePlease";
string BASE_URL = "BaseUrlGoesHerePlease";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddCors();
builder.Services.AddScoped<IDbConnection>(_ => new SqliteConnection(builder.Configuration.GetConnectionString("Database")));
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = "Steam";
})
.AddCookie(options => {
            options.Cookie.SameSite = SameSiteMode.None;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        })
.AddSteam(options =>
{
    options.ApplicationKey = steamApiKey;
    options.CallbackPath = "/signin";
});

var app = builder.Build();

Random random = new();

HttpClient steamApiClient = new() {
    BaseAddress = new Uri("https://api.steampowered.com/"),
};

int bracketSize = 16;

List<int> roundOfSixteenRouteOne = [0,1,8,9,12,13,14];
List<int> qroundOfSixteenRouteTwo = [2,3,9,8,12,13,14];
List<int> roundOfSixteenRouteThree = [4,5,10,11,13,12,14];
List<int> roundOfSixteenRouteFour = [6,7,11,10,13,12,14];
List<int> quaterFinalRouteOne = [8,9,12,13,14,-1,-1];
List<int> quaterFinalRouteTwo = [10,11,13,12,14,-1,-1];
List<int> semiFinalRouteTwo = [12,13,14,-1,-1,-1,-1];

List<List<int>> bracketRoutes = [roundOfSixteenRouteOne, qroundOfSixteenRouteTwo, roundOfSixteenRouteThree, roundOfSixteenRouteFour, quaterFinalRouteOne, quaterFinalRouteTwo, semiFinalRouteTwo];


app.UseAuthentication();

app.UseCors( x => x
    .AllowAnyMethod()
    .AllowAnyHeader()
    .AllowCredentials()
    .WithOrigins(BASE_URL));


app.MapGet("/login", async (HttpContext context, IDbConnection database) => {
    await context.ChallengeAsync("Steam", new AuthenticationProperties {
        RedirectUri = $"{BASE_URL}/postlogin.html"
    });
});

app.MapGet("/postlogin", async (HttpContext context, IDbConnection database) => {

    var user = context.User;
    string? steamId = user.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value.Split("/")[5];
    string? steamNickname = user.Identity.Name;

    string playerAvatarUrl = "";

    string existingUser = database.Query<string>($"SELECT [steamid] FROM [players] WHERE (steamid = {steamId})").FirstOrDefault("none");

    var response = await steamApiClient.GetAsync("ISteamUser/GetPlayerSummaries/v0002/?key=" + steamApiKey + "&steamids=" + steamId);
    var jsonResponse = await response.Content.ReadAsStringAsync();
    JObject steamData = JObject.Parse(jsonResponse);

    string playerNickname = (string)steamData["response"]["players"][0]["personaname"];
    playerAvatarUrl = (string)steamData["response"]["players"][0]["avatarfull"];

    if (existingUser == "none") {
        database.Execute("INSERT INTO [players] VALUES(@steamid, @nickname, @avatar_url)", new
        {
            steamid = steamId,
            nickname = playerNickname,
            avatar_url = playerAvatarUrl
        });
    } else {
        database.Execute($"UPDATE players SET nickname = \'{playerNickname}\', avatar_url = \'{playerAvatarUrl}\' WHERE steamid = \"{steamId}\"");
    }

    return Results.Redirect(BASE_URL);
});

app.MapGet("/debug", async (context) => {

    var user = context.User;
    string? steamId = user.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value.Split("/")[5];
    string? steamNickname = user.Identity.Name;

    if (user.Identity.IsAuthenticated) {
        await context.Response.WriteAsync($"Welcome, {steamNickname}! Your Steam ID is {steamId}. Base URL is {BASE_URL}");
    } else {
        await context.Response.WriteAsync("You are not yet logged tf in");
    }

});

app.MapGet("/profile", async (HttpContext context, IDbConnection database) => {

    var user = context.User;
    string? steamId = user.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value.Split("/")[5];
    string? steamNickname = user.Identity.Name;

    if (user.Identity.IsAuthenticated) {
        string avatarUrl = database.Query<string>($"SELECT [avatar_url] FROM [players] WHERE (steamid = {steamId})").FirstOrDefault("img/no_avatar.jpg");
        int wincount = database.Query<string>($"SELECT [uuid] FROM [matches] WHERE (winner_steamid = {steamId})").Count();

        await context.Response.WriteAsync(profileTemplate(steamNickname, avatarUrl, wincount));
    } else {
        await context.Response.WriteAsync("You are nobody");
    }

});

app.MapGet("/bracket", async (HttpContext context, IDbConnection database) => {

    string date = DateTime.Today.ToString("yyyy-MM-dd");

    // TODO: Refactor this to make less calls
    // database.Query<string>($"SELECT [player_one_steamid],[player_two_steamid] FROM [matches] WHERE (cup_id = {currentCupId}) ORDER BY match_number ASC");
    // THEN PUT THOSE DATAS IN TO A LIST
    int currentCupId = database.Query<int>($"SELECT [id] FROM [cups] WHERE (status = \"ongoing\") OR (status = \"complete\") ORDER BY id DESC LIMIT 1").FirstOrDefault(-1);

    if (currentCupId == -1) {
        await context.Response.WriteAsync("No cups at all");
    }

    List<string> playersInBracket = [];

    for (int i = 0; i < 16; i++) {

        string playerOneSteamId = database.Query<string>($"SELECT [player_one_steamid] FROM [matches] WHERE (match_number = {i}) AND (cup_id = {currentCupId})").FirstOrDefault("");
        playersInBracket.Add(steamIdToNickname(playerOneSteamId, database));
        string playerTwoSteamId = database.Query<string>($"SELECT [player_two_steamid] FROM [matches] WHERE (match_number = {i}) AND (cup_id = {currentCupId})").FirstOrDefault("");
        playersInBracket.Add(steamIdToNickname(playerTwoSteamId, database));

    }

    string cupWinnerName = "";
    string cupWinnerAvatarUrl = "";

    string cupStatus = database.Query<string>($"SELECT [status] FROM [cups] WHERE (id = {currentCupId}) LIMIT 1").First();
    string cupWinnerSteamId = database.Query<string>($"SELECT [winner_steamid] FROM [cups] WHERE (id = {currentCupId}) LIMIT 1").FirstOrDefault("");
    if (cupWinnerSteamId != "") {
        cupWinnerName = steamIdToNickname(cupWinnerSteamId, database);
        cupWinnerAvatarUrl = steamIdToAvatar(cupWinnerSteamId, database);
    }

    await context.Response.WriteAsync(bracketTemplate(playersInBracket, cupStatus, cupWinnerName, cupWinnerAvatarUrl, database));

});

app.MapGet("/createnewcup", (IDbConnection database) => {

    string dateOfCup = DateTime.Today.AddDays(7).ToString("yyyy-MM-dd");

    int openCup = database.Query<int>($"SELECT [id] FROM [cups] WHERE (status = \"open\" OR status = \"ongoing\") LIMIT 1").FirstOrDefault(-1);

    if (openCup != -1) {
        return "There is already an open cup bro";
    } else {

        database.Execute("INSERT INTO [cups] VALUES(NULL, @date, @status, @player_count, @winner_steamid)", new
        {
            date = dateOfCup,
            status = "open",
            player_count  = 0,
            winner_steamid = ""
        });

        int cupID = database.Query<int>($"SELECT [id] FROM [cups] WHERE (status = \"open\") LIMIT 1").FirstOrDefault(0);
        
        return $"New cup created, cup ID is {cupID}";
    }
});

app.MapGet("/register", (HttpContext context, IDbConnection database) => {

    string? steamId = context.User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value.Split("/")[5];
    int currentCupId = database.Query<int>($"SELECT [id] FROM [cups] WHERE (status = \"open\") LIMIT 1").FirstOrDefault(-1);

    IEnumerable<string> registeredPlayers = database.Query<string>($"SELECT [player_steamid] FROM [cup_player_lists] WHERE (cup_id = {currentCupId})");
    int numberOfRegisteredPlayers = registeredPlayers.Count();

    if ( numberOfRegisteredPlayers >= bracketSize) {
        return "Bracket is full, sorry, return next week but earlier";
    } else if (currentCupId == -1) {
        return "No cups open for registration at this present time";
    } else {

        IEnumerable<string> playersInPool = database.Query<string>($"SELECT [player_steamid] FROM [cup_player_lists] WHERE (cup_id = {currentCupId})");

        if (!playersInPool.Contains(steamId)) {

            database.Execute("INSERT INTO [cup_player_lists] VALUES(@cup_id, @player_steamid)", new
            {
                cup_id = currentCupId,
                player_steamid = steamId
            });

            return "Registered";

        } else {
            return "You are already registered friend";
        }
    }
});

app.MapGet("/registertestplayers", (IDbConnection database) => {
    List<string> testPlayers = [
    "76561198023456789",
    "76561197987654321",
    "76561198034567890"
    ];

    int currentCupId = database.Query<int>($"SELECT [id] FROM [cups] WHERE (status = \"open\") LIMIT 1").FirstOrDefault(-1);

    foreach (var testPlayer in testPlayers) {
    database.Execute("INSERT INTO [cup_player_lists] VALUES(@cup_id, @player_steamid)", new
    {
        cup_id = currentCupId,
        player_steamid = testPlayer
    });
    }
    return "Test players added";

});

app.MapGet("/generatebracket", (IDbConnection database) => {

        int currentCupId = database.Query<int>($"SELECT [id] FROM [cups] WHERE (status = \"open\") LIMIT 1").FirstOrDefault(-1);

        List<string> listOfRegisteredPlayers = database.Query<string>($"SELECT [player_steamid] FROM [cup_player_lists] WHERE (cup_id = \"{currentCupId}\")").ToList();

        int numberOfPlayers = listOfRegisteredPlayers.Count;

        if (numberOfPlayers < 16) {
            int numberOfPlayersToAdd = 16 - numberOfPlayers;
            for (int i = 0; i < numberOfPlayersToAdd; i++) {
                listOfRegisteredPlayers.Add("NO_OPPONENT");
            }
            numberOfPlayers = listOfRegisteredPlayers.Count;
        }

        for (int i = 0; i < (numberOfPlayers / 2); i++) {

            int randomInteger1 = random.Next(listOfRegisteredPlayers.Count);
            string playerOne = listOfRegisteredPlayers[randomInteger1];
            listOfRegisteredPlayers.RemoveAt(randomInteger1);

            int randomInteger2 = random.Next(listOfRegisteredPlayers.Count);
            string playerTwo = listOfRegisteredPlayers[randomInteger2];
            listOfRegisteredPlayers.RemoveAt(randomInteger2);

            generateMatch(i, playerOne, playerTwo, database);
        }

        // TODO: This in a more efficient way which I imagine there is
        for (int matchNumber = 0; matchNumber < 14; matchNumber++) {
            foreach (var route in bracketRoutes) {
                if (matchNumber == route[0] && database.Query<string>($"SELECT [status] FROM [matches] WHERE match_number = {route[0]} AND cup_id = {currentCupId}").FirstOrDefault("") == "complete") {
                    if (database.Query<string>($"SELECT [status] FROM [matches] WHERE match_number = {route[1]} AND cup_id = {currentCupId}").FirstOrDefault("") == "complete") {
                        string winnerOfLeftLeg = database.Query<string>($"SELECT [winner_steamid] FROM [matches] WHERE match_number = {route[0]} AND cup_id = {currentCupId}").First();
                        string winnerOfRightLeg = database.Query<string>($"SELECT [winner_steamid] FROM [matches] WHERE match_number = {route[1]} AND cup_id = {currentCupId}").First();
                        generateMatch(route[2], winnerOfLeftLeg, winnerOfRightLeg, database);
                    }
                }
            }
        }

        database.Execute($"UPDATE cups SET status = \"ongoing\" WHERE id = {currentCupId}");

        return $"Bracket generated :)\n";
});

app.MapGet("/match", async (HttpContext context, IDbConnection database) => {

    bool userIsPlayerOne;
    var user = context.User;
    string? steamId = user.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value.Split("/")[5];

    if (!user.Identity.IsAuthenticated) {
        await context.Response.WriteAsync("You are not logged in. Please log in via steam to view your matches");
    } else {

        string uuid = database.Query<string>($"SELECT [uuid] FROM [matches] WHERE (status = \"pending\" OR status= \"waiting_p1\" OR status= \"waiting_p2\") AND ((player_one_steamid = {steamId}))").FirstOrDefault("none");

        if (uuid == "none") {
            userIsPlayerOne = false;
            uuid = database.Query<string>($"SELECT [uuid] FROM [matches] WHERE (status = \"pending\" OR status= \"waiting_p1\" OR status= \"waiting_p2\") AND ((player_two_steamid = {steamId}))").FirstOrDefault("none");
        } else {
            userIsPlayerOne = true;
        }

        // TODO: Can I refactor all queries in this to one call?
        string matchStatus = database.Query<string>($"SELECT [status] FROM [matches] WHERE uuid = \'{uuid}\'").FirstOrDefault("none");

        if (uuid == "none") {
            await context.Response.WriteAsync(noMatchTemplate(API_URL));
        } else if (userIsPlayerOne && matchStatus == "waiting_p2") {
            await context.Response.WriteAsync(waitingForMatchResultTemplate(API_URL));
        } else if (!userIsPlayerOne && matchStatus == "waiting_p1") {
            await context.Response.WriteAsync(waitingForMatchResultTemplate(API_URL));
        } else {
            string playerOne = database.Query<string>($"SELECT [player_one_steamid] FROM [matches] WHERE (status = \"pending\" OR status= \"waiting_p1\" OR status= \"waiting_p2\") AND ((player_one_steamid = {steamId}) OR (player_two_steamid = {steamId}))").First();
            string playerTwo = database.Query<string>($"SELECT [player_two_steamid] FROM [matches] WHERE (status = \"pending\" OR status= \"waiting_p1\" OR status= \"waiting_p2\") AND ((player_one_steamid = {steamId}) OR (player_two_steamid = {steamId}))").First();
            string sharedWord = database.Query<string>($"SELECT [shared_word] FROM [matches] WHERE (uuid = \'{uuid}\')").FirstOrDefault("none");
            string opponentName;
            string opponentAvatar;

            if (playerOne == steamId) {
                opponentName = steamIdToNickname(playerTwo, database);
                opponentAvatar = database.Query<string>($"SELECT [avatar_url] FROM [players] WHERE (steamid = {playerTwo})").FirstOrDefault("none");
                await context.Response.WriteAsync(openMatchHostTemplate(API_URL, uuid, opponentName, opponentAvatar, sharedWord));
            } else {
                opponentName = steamIdToNickname(playerOne, database);
                opponentAvatar = database.Query<string>($"SELECT [avatar_url] FROM [players] WHERE (steamid = {playerOne})").FirstOrDefault("none");
                await context.Response.WriteAsync(openMatchClientTemplate(API_URL, uuid, opponentName, opponentAvatar, sharedWord));
            }

        }
    }
});

app.MapGet("/resultverification", ([FromQuery(Name = "result")] string result, HttpContext context, IDbConnection database) => {
    
    return resultConfirmationTemplate(API_URL, result);
});

// TODO: Do this in a better way
app.MapGet("/submitmatchresult", ([FromQuery(Name = "result")] string result, HttpContext context, IDbConnection database) => {

    bool userIsPlayerOne;
    string? steamId = context.User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value.Split("/")[5];

    string matchuuid = database.Query<string>($"SELECT [uuid] FROM [matches] WHERE (status = \"pending\" OR status= \"waiting_p1\" OR status= \"waiting_p2\") AND ((player_one_steamid = {steamId}))").FirstOrDefault("none");

    if (matchuuid == "none") {
        userIsPlayerOne = false;
        matchuuid = database.Query<string>($"SELECT [uuid] FROM [matches] WHERE (status = \"pending\" OR status= \"waiting_p1\" OR status= \"waiting_p2\") AND ((player_two_steamid = {steamId}))").FirstOrDefault("none");
    } else {
        userIsPlayerOne = true;
    }

    // TODO: Get score from user form query string and add to DB
    
    if (userIsPlayerOne) {
        database.Execute($"UPDATE matches SET player_one_declared_result = \'{result}\' WHERE UUID = \'{matchuuid}\'");
    } else {
        database.Execute($"UPDATE matches SET player_two_declared_result = \'{result}\' WHERE UUID = \'{matchuuid}\'");
    }

    // // GET THIS WORKING
    // IEnumerable<string> declaredResults = database.Query<string>($"SELECT [player_one_declared_result],[player_two_declared_result] FROM [matches] WHERE UUID = \'{matchuuid}\'");
    // string playerOneResult = declaredResults.ElementAt(0);
    // string playerTwoResult = declaredResults.ElementAt(1);

    string playerOneResult = database.Query<string>($"SELECT [player_one_declared_result],[player_two_declared_result] FROM [matches] WHERE UUID = \'{matchuuid}\'").FirstOrDefault("");
    string playerTwoResult = database.Query<string>($"SELECT [player_two_declared_result],[player_two_declared_result] FROM [matches] WHERE UUID = \'{matchuuid}\'").FirstOrDefault("");
    
    if (playerOneResult != "" && playerTwoResult != "") {
        if (playerOneResult == playerTwoResult) {
            database.Execute($"UPDATE matches SET winner_steamid = \"FORFEIT\" WHERE uuid = \'{matchuuid}\'");
        } else if (playerOneResult == "winner") {
            string playerOneSteamId = database.Query<string>($"SELECT [player_one_steamid] FROM [matches] WHERE UUID = \'{matchuuid}\'").First();
            database.Execute($"UPDATE matches SET winner_steamid = {playerOneSteamId} WHERE uuid = \'{matchuuid}\'");
        } else if (playerTwoResult == "winner") {
            string playerTwoSteamId = database.Query<string>($"SELECT [player_two_steamid] FROM [matches] WHERE UUID = \'{matchuuid}\'").First();
            database.Execute($"UPDATE matches SET winner_steamid = {playerTwoSteamId} WHERE uuid = \'{matchuuid}\'");
        }
        database.Execute($"UPDATE matches SET status = \"complete\" WHERE uuid = \'{matchuuid}\'");
    } else if (playerOneResult == "") {
        database.Execute($"UPDATE matches SET status = \"waiting_p1\" WHERE uuid = \'{matchuuid}\'");
    } else if (playerTwoResult == "") {
        database.Execute($"UPDATE matches SET status = \"waiting_p2\" WHERE uuid = \'{matchuuid}\'");
    }

    int matchNumber = database.Query<int>($"SELECT [match_number] FROM [matches] WHERE uuid = \'{matchuuid}\'").First();

    int currentCupId = database.Query<int>($"SELECT [id] FROM [cups] WHERE (status = \"ongoing\") LIMIT 1").FirstOrDefault(0);

    // TODO: Make this more efficient I think. Recursive function?
    foreach (var route in bracketRoutes) {
        if (matchNumber == route[0] && database.Query<string>($"SELECT [status] FROM [matches] WHERE match_number = {route[0]} AND cup_id = {currentCupId}").First() == "complete") {
            if (database.Query<string>($"SELECT [status] FROM [matches] WHERE match_number = {route[1]} AND cup_id = {currentCupId}").First() == "complete") {
                string winnerOfLeftLeg = database.Query<string>($"SELECT [winner_steamid] FROM [matches] WHERE match_number = {route[0]} AND cup_id = {currentCupId}").First();
                string winnerOfRightLeg = database.Query<string>($"SELECT [winner_steamid] FROM [matches] WHERE match_number = {route[1]} AND cup_id = {currentCupId}").First();
                generateMatch(route[2], winnerOfLeftLeg, winnerOfRightLeg, database);
            } else {
                return "Awaiting result of other match";
            }
        } else if (matchNumber == route[1] && database.Query<string>($"SELECT [status] FROM [matches] WHERE match_number = {route[1]} AND cup_id = {currentCupId}").First() == "complete") {
            if (database.Query<string>($"SELECT [status] FROM [matches] WHERE match_number = {route[0]} AND cup_id = {currentCupId}").First() == "complete") {
                string winnerOfLeftLeg = database.Query<string>($"SELECT [winner_steamid] FROM [matches] WHERE match_number = \'{route[0]}\' AND cup_id = {currentCupId}").First();
                string winnerOfRightLeg = database.Query<string>($"SELECT [winner_steamid] FROM [matches] WHERE match_number = \'{route[1]}\' AND cup_id = {currentCupId}").First();
                generateMatch(route[2], winnerOfLeftLeg, winnerOfRightLeg, database);
            } else {
                return "Awaiting result of other match";
            }
        }
        // CHECK REST OF BRACKET ROUTE HERE
        if (database.Query<string>($"SELECT [status] FROM [matches] WHERE match_number = {route[2]} AND cup_id = {currentCupId}").FirstOrDefault("") == "complete" && route[3] != -1) {
            if (database.Query<string>($"SELECT [status] FROM [matches] WHERE match_number = {route[3]} AND cup_id = {currentCupId}").FirstOrDefault("") == "complete") {
                string winnerOfLeftLeg = database.Query<string>($"SELECT [winner_steamid] FROM [matches] WHERE match_number = \'{route[2]}\' AND cup_id = {currentCupId}").First();
                string winnerOfRightLeg = database.Query<string>($"SELECT [winner_steamid] FROM [matches] WHERE match_number = \'{route[3]}\' AND cup_id = {currentCupId}").First();
                generateMatch(route[4], winnerOfLeftLeg, winnerOfRightLeg, database);
                if (database.Query<string>($"SELECT [status] FROM [matches] WHERE match_number = {route[4]} AND cup_id = {currentCupId}").FirstOrDefault("") == "complete" && route[5] != -1) {
                    if (database.Query<string>($"SELECT [status] FROM [matches] WHERE match_number = {route[5]} AND cup_id = {currentCupId}").FirstOrDefault("") == "complete") {
                        winnerOfLeftLeg = database.Query<string>($"SELECT [winner_steamid] FROM [matches] WHERE match_number = \'{route[4]}\' AND cup_id = {currentCupId}").First();
                        winnerOfRightLeg = database.Query<string>($"SELECT [winner_steamid] FROM [matches] WHERE match_number = \'{route[5]}\' AND cup_id = {currentCupId}").First();
                        generateMatch(route[6], winnerOfLeftLeg, winnerOfRightLeg, database);
                    }
            }
        }
        }
    }
    if (matchNumber == 14 && database.Query<string>($"SELECT [status] FROM [matches] WHERE match_number = 14 AND cup_id = {currentCupId}").First() == "complete") {
        string cupWinner = database.Query<string>($"SELECT [winner_steamid] FROM [matches] WHERE match_number = 14 AND cup_id = {currentCupId}").First();
        Console.WriteLine(cupWinner);
        database.Execute($"UPDATE cups SET winner_steamid = \"{cupWinner}\", status = \"complete\" WHERE status = \"ongoing\"");
    }

    return noMatchTemplate(API_URL);
});

app.MapGet("/reportplayer", (IDbConnection database) => {
    // Notify me somehow of the steamid of the reporter and the reportee and I can manually investigate and ban them if needed
});

static bool generateMatch(int matchNumber, string playerOneSteamID, string playerTwoSteamID, IDbConnection database) {

    if (matchNumber == -1){
        return false;
    }

    int currentCupId = database.Query<int>($"SELECT [id] FROM [cups] WHERE (status = \"open\" OR status = \"ongoing\") LIMIT 1").First();
    string matchToBeCreated = database.Query<string>($"SELECT [match_number] FROM [matches] WHERE (cup_id = {currentCupId} AND match_number = {matchNumber}) LIMIT 1").FirstOrDefault("");
    if (matchToBeCreated != "") {
        Console.WriteLine($"Match number {matchToBeCreated} already exists for {currentCupId}");
        return false;
    }
    

    Guid UUID = Guid.NewGuid();
    string date = DateTime.Today.ToString("yyyy-MM-dd");
    string winner;
    string status;
    RandomWord randomword = new();

    if (playerOneSteamID == "FORFEIT" || playerOneSteamID == "NO_OPPONENT") {
        winner = playerTwoSteamID;
        status = "complete";
    } else if (playerTwoSteamID == "FORFEIT" || playerTwoSteamID == "NO_OPPONENT") {
        winner = playerOneSteamID;
        status = "complete";
    } else {
        winner = "";
        status = "pending";
    }

    Match match = new() {
        UUID = UUID,
        date = date,
        cupID = currentCupId,
        matchNumber = matchNumber,
        status = status,
        playerOneSteamID = playerOneSteamID,
        playerTwoSteamID = playerTwoSteamID,
        playerOneDeclaredResult = "",
        playerTwoDeclaredResult = "",
        winnerSteamID = winner,
        score = "",
        sharedWord = randomword.getRandomWord()
    };

    database.Execute("INSERT INTO [matches] VALUES(@uuid, @date, @cup_id, @match_number, @status, @player_one_steamid, @player_two_steamid, @winner_steamid, @player_one_declared_result, @player_two_declared_result, @score, @shared_word)", new
    {
        uuid = match.UUID,
        date = match.date,
        cup_id = match.cupID,
        match_number = match.matchNumber,
        status = match.status,
        player_one_steamid = match.playerOneSteamID,
        player_two_steamid = match.playerTwoSteamID,
        player_one_declared_result = match.playerOneDeclaredResult,
        player_two_declared_result = match.playerTwoDeclaredResult,
        winner_steamid = match.winnerSteamID,
        score = match.score,
        shared_word = match.sharedWord
    });

    return true;
}

static string steamIdToNickname(string steamId, IDbConnection database) {
    if (steamId == "") {
        return "";
    } else if (steamId == "FORFEIT" || steamId == "NO_OPPONENT") {
        return "No Opponent";
    } else {
    return database.Query<string>($"SELECT [nickname] FROM [players] WHERE steamid = \'{steamId}\'").First();
    }
}

static string steamIdToAvatar(string steamId, IDbConnection database) {
    if (steamId == "") {
        return "";
    } else if (steamId == "FORFEIT" || steamId == "NO_OPPONENT") {
        return "SOMEURLHERE";
    } else {
    return database.Query<string>($"SELECT [avatar_url] FROM [players] WHERE steamid = \'{steamId}\'").First();
    }
}

static string profileTemplate(string profileName, string avatarUrl, int wincount) {
    return @$"
    <div class=""centre"">
        <h3>{profileName}</h3>
        <img src=""{avatarUrl}"">
        <p>Match wins: {wincount}</p>
    </div>";
}

static string noMatchTemplate(string API_URL) {
    return @$"
    <div class=""centre"" id=""match""
      hx-get=""{API_URL}/match""
      hx-request='{"credentials": true}' 
      hx-target=""#match""
      hx-swap=""outerHTML""
      hx-trigger=""every 10s"">
        <h3>You currently have no match to play. This page will update once a match is available, no need to refresh</h3>
    </div>";
}

static string waitingForMatchResultTemplate(string API_URL) {
    return @$"
    <div class=""centre"" id=""match""
      hx-get=""{API_URL}/match""
      hx-request='{"credentials": true}' 
      hx-target=""#match""
      hx-swap=""outerHTML""
      hx-trigger=""every 10s"">
        <h3>Thanks for submitting the result, we are now awaiting your opponent to do the same</h3>
    </div>";
}

static string openMatchHostTemplate(string API_URL, string uuid, string opponentName, string opponentAvatar, string sharedword) {
    return @$"
    <div class=""centre"" id=""match""
      hx-get=""{API_URL}/match""
      hx-request='{"credentials": true}' 
      hx-target=""#match""
      hx-swap=""outerHTML""
      hx-trigger=""every 10s"">
        <h3>Your opponent is:</h3>
        <img src=""{opponentAvatar}"">
        <p>{opponentName}</p>
        <p>Please host a public lobby and wait for {opponentName} to join. You'll know it is actually them as they will type the codeword '{sharedword}' in the chat</p>
        <br>
        <div id=""result_submission"">
            <p>Once complete, submit the result below</p>
            <button 
            style=""background-color: green;""
            hx-get=""{API_URL}/resultverification?result=winner""
            hx-target=""#result_submission""
            hx-swap=""innerHTML"">Win</button> 
            <button 
            style=""background-color: red;""
            hx-get=""{API_URL}/resultverification?result=loser""
            hx-target=""#result_submission""
            hx-swap=""innerHTML"">Loss</button> 
        </div>
    </div>";
}

static string openMatchClientTemplate(string API_URL, string uuid, string opponentName, string opponentAvatar, string sharedword) {
    return @$"
    <div class=""centre"" id=""match""
      hx-get=""{API_URL}/match""
      hx-request='{"credentials": true}' 
      hx-target=""#match""
      hx-swap=""outerHTML""
      hx-trigger=""every 10s"">
        <h3>Your opponent is:</h3>
        <img src=""{opponentAvatar}"">
        <p>{opponentName}</p>
        <p> {opponentName} will host a public lobby. Please join it and type the codeword '{sharedword}' to confirm to them that you are the right person </p>
        <br>
        <div id=""result_submission"">
            <p>Once complete, submit the result below</p>
            <button 
            style=""background-color: green;""
            hx-get=""{API_URL}/resultverification?result=winner""
            hx-target=""#result_submission""
            hx-swap=""innerHTML"">Win</button> 
            <button 
            style=""background-color: red;""
            hx-get=""{API_URL}/resultverification?result=loser""
            hx-target=""#result_submission""
            hx-swap=""innerHTML"">Loss</button> 
        </div>
    </div>";
}

static string resultConfirmationTemplate(string API_URL, string declaredResult) {
    return @$"
    <div class=""centre"" id=""result_submission_confirmation"">
        <p>You are about to declare yourself as the {declaredResult}, is this correct?</p>
        <button 
            style=""background-color: lightskyblue;""
            hx-get=""{API_URL}/submitmatchresult?result={declaredResult}""
            hx-target=""#match""
            hx-swap=""outerHTML"">Yes
        </button> 
        <button 
            style=""background-color: lightcoral;""
            hx-get=""{API_URL}/match""
            hx-target=""#match""
            hx-swap=""outerHTML"">Back
        </button> 
    </div>
    ";
}

static string bracketTemplate(List<string> playersInBracket, string cupStatus, string cupWinnerName, string cupWinnerAvatarUrl, IDbConnection database) {
    
    string response = @$"
    <div class=""centre"" id=""bracket"">
    <div class=""playoff-table"">
    <div class=""playoff-table-content"">
        <div class=""playoff-table-tour"">
            <div class=""playoff-table-group"">
                <div class=""playoff-table-pair playoff-table-pair-style"">
                    <div class=""playoff-table-left-player"">{playersInBracket.ElementAt(0)}</div>
                    <div class=""playoff-table-right-player"">{playersInBracket.ElementAt(1)}</div>
                </div>
                <div class=""playoff-table-pair playoff-table-pair-style"">
                    <div class=""playoff-table-left-player"">{playersInBracket.ElementAt(2)}</div>
                    <div class=""playoff-table-right-player"">{playersInBracket.ElementAt(3)}</div>
                </div>
            </div>
            <div class=""playoff-table-group"">
                <div class=""playoff-table-pair playoff-table-pair-style"">
                    <div class=""playoff-table-left-player"">{playersInBracket.ElementAt(4)}</div>
                    <div class=""playoff-table-right-player"">{playersInBracket.ElementAt(5)}</div>
                </div>
                <div class=""playoff-table-pair playoff-table-pair-style"">
                    <div class=""playoff-table-left-player"">{playersInBracket.ElementAt(6)}</div>
                    <div class=""playoff-table-right-player"">{playersInBracket.ElementAt(7)}</div>
                </div>
            </div>
            <div class=""playoff-table-group"">
                <div class=""playoff-table-pair playoff-table-pair-style"">
                    <div class=""playoff-table-left-player"">{playersInBracket.ElementAt(8)}</div>
                    <div class=""playoff-table-right-player"">{playersInBracket.ElementAt(9)}</div>
                </div>
                <div class=""playoff-table-pair playoff-table-pair-style"">
                    <div class=""playoff-table-left-player"">{playersInBracket.ElementAt(10)}</div>
                    <div class=""playoff-table-right-player"">{playersInBracket.ElementAt(11)}</div>
                </div>
            </div>
            <div class=""playoff-table-group"">
                <div class=""playoff-table-pair playoff-table-pair-style"">
                    <div class=""playoff-table-left-player"">{playersInBracket.ElementAt(12)}</div>
                    <div class=""playoff-table-right-player"">{playersInBracket.ElementAt(13)}</div>
                </div>
                <div class=""playoff-table-pair playoff-table-pair-style"">
                    <div class=""playoff-table-left-player"">{playersInBracket.ElementAt(14)}</div>
                    <div class=""playoff-table-right-player"">{playersInBracket.ElementAt(15)}</div>
                </div>
            </div>
        </div>
        <div class=""playoff-table-tour"">
            <div class=""playoff-table-group"">
                <div class=""playoff-table-pair playoff-table-pair-style"">
                    <div class=""playoff-table-left-player"">{playersInBracket.ElementAt(16)}</div>
                    <div class=""playoff-table-right-player"">{playersInBracket.ElementAt(17)}</div>
                </div>
                <div class=""playoff-table-pair playoff-table-pair-style"">
                    <div class=""playoff-table-left-player"">{playersInBracket.ElementAt(18)}</div>
                    <div class=""playoff-table-right-player"">{playersInBracket.ElementAt(19)}</div>
                </div>
            </div>
            <div class=""playoff-table-group"">
                <div class=""playoff-table-pair playoff-table-pair-style"">
                    <div class=""playoff-table-left-player"">{playersInBracket.ElementAt(20)}</div>
                    <div class=""playoff-table-right-player"">{playersInBracket.ElementAt(21)}</div>
                </div>
                <div class=""playoff-table-pair playoff-table-pair-style"">
                    <div class=""playoff-table-left-player"">{playersInBracket.ElementAt(22)}</div>
                    <div class=""playoff-table-right-player"">{playersInBracket.ElementAt(23)}</div>
                </div>
            </div>
        </div>
        <div class=""playoff-table-tour"">
            <div class=""playoff-table-group"">
                <div class=""playoff-table-pair playoff-table-pair-style"">
                    <div class=""playoff-table-left-player"">{playersInBracket.ElementAt(24)}</div>
                    <div class=""playoff-table-right-player"">{playersInBracket.ElementAt(25)}</div>
                </div>
                <div class=""playoff-table-pair playoff-table-pair-style"">
                    <div class=""playoff-table-left-player"">{playersInBracket.ElementAt(26)}</div>
                    <div class=""playoff-table-right-player"">{playersInBracket.ElementAt(27)}</div>
                </div>
            </div>
        </div>
        <div class=""playoff-table-tour"">
            <div class=""playoff-table-group"">
                <div class=""playoff-table-pair playoff-table-pair-style"">
                    <div class=""playoff-table-left-player"">{playersInBracket.ElementAt(28)}</div>
                    <div class=""playoff-table-right-player"">{playersInBracket.ElementAt(29)}</div>
                </div>
            </div>
        </div>
    </div>
</div>
</div>";

if (cupStatus == "complete") {
    response += @$"
        <div class=""centre"" id=""bracket_winner_wrapper"">
    <div id=""bracket_winner"">
    <p>Winner!</p>
    <img src=""{cupWinnerAvatarUrl}"">
    <p>{cupWinnerName}</p>
    </div>
    </div>
    ";
}
return response;

}

app.Run();

public struct Match { 
    public Guid UUID;
    public string date;
    public int cupID;
    public int matchNumber;
    public string status;
    public string playerOneSteamID;
    public string playerTwoSteamID;
    public string playerOneDeclaredResult;
    public string playerTwoDeclaredResult;
    public string winnerSteamID;
    public string score;
    public string sharedWord;
}

// TABLES I WILL NEED WITH WHAT DATA
// Players - SteamID, nickname, steam avatar, wincount
// Matches - UUID, date, cup_id, status(pending, complete) playerOneSteamId, playerTwoSteamID, winner(blank atm), score
// Cups - ID, date, number of players, winner
