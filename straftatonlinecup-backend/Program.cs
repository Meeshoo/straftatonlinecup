using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Dapper;
using Microsoft.Data.Sqlite;
using System.Data;
using SameSiteMode = Microsoft.AspNetCore.Http.SameSiteMode;
using Newtonsoft.Json.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;

string steamApiKey = "PutSteamKeyHerePlease";
string API_URL = "ApiUrlGoesHerePlease";
string BASE_URL = "BaseUrlGoesHerePlease";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddCors();
builder.Services.AddScoped<IDbConnection>(_ => new SqliteConnection(builder.Configuration.GetConnectionString("Database")));
builder.Services.AddAuthentication(options => {
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = "Steam";
})
.AddCookie(options => {
            options.Cookie.SameSite = SameSiteMode.None;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        })
.AddSteam(options => {
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

    playerAvatarUrl = (string)steamData["response"]["players"][0]["avatarfull"];

    if (existingUser == "none") {
        database.Execute("INSERT INTO [players] VALUES(@steamid, @nickname, @avatar_url)", new
        {
            steamid = steamId,
            nickname = steamNickname,
            avatar_url = playerAvatarUrl
        });
    } else {
        database.Execute($"UPDATE players SET nickname = \'{steamNickname}\', avatar_url = \'{playerAvatarUrl}\' WHERE steamid = \"{steamId}\"");
    }

    return $"<script>window.location.replace(\"{BASE_URL}\")</script>";

});

app.MapGet("/headerprofile", (HttpContext context, IDbConnection database) => {

    var user = context.User;
    string? steamId = user.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value.Split("/")[5];

    if (user.Identity.IsAuthenticated) {
        string? steamNickname = user.Identity.Name;
        string avatarUrl = database.Query<string>($"SELECT [avatar_url] FROM [players] WHERE (steamid = {steamId})").FirstOrDefault("img/no_avatar.jpg");
        return headerProfileTemplate(steamNickname, avatarUrl);
    } else {
        return $"<a href=\"{API_URL}/login\"><img style=\"margin-top: 12%;\" src=\"img/SteamSignin.png\"></a>";
    }
});

app.MapGet("/debug", async (context) => {

    var user = context.User;
    string? steamId = user.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value.Split("/")[5];
    string? steamNickname = user.Identity.Name;

    if (user.Identity.IsAuthenticated) {
        await context.Response.WriteAsync($"Welcome, {steamNickname}! Your Steam ID is {steamId}.");
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
        int wincount = database.Query<string>($"SELECT [uuid] FROM [matches] WHERE winner_steamid = {steamId} AND NOT player_one_steamid = \"NO_OPPONENT\" AND NOT player_two_steamid = \"NO_OPPONENT\" AND NOT player_one_steamid = \"FORFEIT\" AND NOT player_two_steamid = \"FORFEIT\"").Count();

        await context.Response.WriteAsync(profileTemplate(steamNickname, avatarUrl, wincount));
    } else {
        await context.Response.WriteAsync("You are nobody");
    }

});

app.MapGet("/getcurrentcup", async (HttpContext context, IDbConnection database) => {

    string currentCupStatus = database.Query<string>($"SELECT [status] FROM [cups] ORDER BY id DESC LIMIT 1").FirstOrDefault("");

    if (currentCupStatus == "open") {
        int currentOpenCupId = database.Query<int>($"SELECT [id] FROM [cups] WHERE (status = \"open\") ORDER BY id DESC LIMIT 1").First();
        await context.Response.WriteAsync(openCupTemplate(API_URL, currentOpenCupId));
    } else if (currentCupStatus == "ongoing") {
        
        int currentOngoingCupId = database.Query<int>($"SELECT [id] FROM [cups] WHERE (status = \"ongoing\") ORDER BY id DESC LIMIT 1").First();
        List<string> playersInBracket = getPlayersInBracket(currentOngoingCupId, bracketSize, database);

        await context.Response.WriteAsync(bracketTemplate(playersInBracket, $"Cup #{currentOngoingCupId} - Ongoing", "ongoing", "", "", database));
    } else {
        await context.Response.WriteAsync("No cups, ever");
    }

});

app.MapGet("/getpreviouscup", async (HttpContext context, IDbConnection database) => {

    int currentOpenCupId = database.Query<int>($"SELECT [id] FROM [cups] WHERE (status = \"open\") ORDER BY id DESC LIMIT 1").FirstOrDefault(-1);
    if (currentOpenCupId == -1) {
        await context.Response.WriteAsync("");
        return;
    }

    int completeCupId = database.Query<int>($"SELECT [id] FROM [cups] WHERE (status = \"complete\") ORDER BY id DESC LIMIT 1").FirstOrDefault(-1);

    if (completeCupId != -1) {
        string completeCupDate = database.Query<string>($"SELECT [date] FROM [cups] WHERE id = \"{completeCupId}\"").First();
        string cupWinnerName = "";
        string cupWinnerAvatarUrl = "";
        string cupWinnerSteamId = database.Query<string>($"SELECT [winner_steamid] FROM [cups] WHERE (id = {completeCupId}) LIMIT 1").FirstOrDefault("");
        if (cupWinnerSteamId != "") {
            cupWinnerName = steamIdToNickname(cupWinnerSteamId, database);
            cupWinnerAvatarUrl = steamIdToAvatar(cupWinnerSteamId, database);
        }
        List<string> playersInBracket = getPlayersInBracket(completeCupId, bracketSize, database);
        string bracketTitle = $"Cup #{completeCupId} - {completeCupDate}";
        await context.Response.WriteAsync(bracketTemplate(playersInBracket, bracketTitle, "complete", cupWinnerName, cupWinnerAvatarUrl, database));
    } else {
        await context.Response.WriteAsync("");
    }

});

app.MapGet("/getpastfivecups", async (HttpContext context, IDbConnection database) => {

    IEnumerable<int> completeCupIds = database.Query<int>($"SELECT [id] FROM [cups] WHERE (status = \"complete\") ORDER BY id DESC LIMIT 5").DefaultIfEmpty(-1);

    string response = "";

    if (completeCupIds.ElementAt(0) != -1) {
        foreach (var cupId in completeCupIds) {
            string completeCupDate = database.Query<string>($"SELECT [date] FROM [cups] WHERE id = \"{cupId}\"").First();
            string cupWinnerSteamId = database.Query<string>($"SELECT [winner_steamid] FROM [cups] WHERE (id = {cupId}) LIMIT 1").FirstOrDefault("");
            string cupWinnerName = steamIdToNickname(cupWinnerSteamId, database);
            string cupWinnerAvatarUrl = steamIdToAvatar(cupWinnerSteamId, database);

            List<string> playersInBracket = getPlayersInBracket(cupId, bracketSize, database);

            string bracketTitle = $"Cup #{cupId} - {completeCupDate}";
            response += bracketTemplate(playersInBracket, bracketTitle, "complete", cupWinnerName, cupWinnerAvatarUrl, database);
        }
        await context.Response.WriteAsync(response);
    } else {
        await context.Response.WriteAsync("<h3 class=\"centre\">No previous cups completed</h3>");
    }

});

// TODO: Limit endpoint to localhost
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

    var user = context.User;

    string? steamId = user.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value.Split("/")[5];
    int currentCupId = database.Query<int>($"SELECT [id] FROM [cups] WHERE (status = \"open\") LIMIT 1").FirstOrDefault(-1);

    IEnumerable<string> registeredPlayers = database.Query<string>($"SELECT [player_steamid] FROM [cup_player_lists] WHERE (cup_id = {currentCupId})");
    int numberOfRegisteredPlayers = registeredPlayers.Count();

    if ( numberOfRegisteredPlayers >= bracketSize) {
        return "Bracket is full, sorry, return next week but earlier";
    } else if (currentCupId == -1) {
        return "No cups open for registration at this present time";
    } else if (!user.Identity.IsAuthenticated) {
        return "Please log in to register";
    } else {

        IEnumerable<string> playersInPool = database.Query<string>($"SELECT [player_steamid] FROM [cup_player_lists] WHERE (cup_id = {currentCupId})");

        if (!playersInPool.Contains(steamId)) {

            database.Execute("INSERT INTO [cup_player_lists] VALUES(@cup_id, @player_steamid)", new
            {
                cup_id = currentCupId,
                player_steamid = steamId
            });

            return "Registered, please wait for your match to show on the match page";

        } else {
            return "You are already registered friend";
        } 
    }
});

// TODO: Limit endpoint to localhost
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
        await context.Response.WriteAsync("<h3 class=\"centre\">You are not logged in. Please log in via steam to view your matches</h3>");
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
    

    Guid uuid = Guid.NewGuid();
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
        uuid = uuid,
        date = date,
        cup_id = currentCupId,
        match_number = matchNumber,
        status = status,
        player_one_steamid = playerOneSteamID,
        player_two_steamid = playerTwoSteamID,
        player_one_declared_result = "",
        player_two_declared_result = "",
        winner_steamid = winner,
        score = "",
        shared_word = randomword.getRandomWord()
    };

    database.Execute("INSERT INTO [matches] VALUES(@uuid, @date, @cup_id, @match_number, @status, @player_one_steamid, @player_two_steamid, @winner_steamid, @player_one_declared_result, @player_two_declared_result, @score, @shared_word)", new
    {
        match.uuid,
        match.date,
        match.cup_id,
        match.match_number,
        match.status,
        match.player_one_steamid,
        match.player_two_steamid,
        match.player_one_declared_result,
        match.player_two_declared_result,
        match.winner_steamid,
        match.score,
        match.shared_word
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

static List<string> getPlayersInBracket(int cupId, int bracketSize, IDbConnection database) {
        
        List<string> playersInBracket = [];

        // TODO: Refactor this to make less calls
        // database.Query<string>($"SELECT [player_one_steamid],[player_two_steamid] FROM [matches] WHERE (cup_id = {currentCupId}) ORDER BY match_number ASC");
        // THEN PUT THOSE DATAS IN TO A LIST
        for (int i = 0; i < bracketSize; i++) {

            string playerOneSteamId = database.Query<string>($"SELECT [player_one_steamid] FROM [matches] WHERE (match_number = {i}) AND (cup_id = {cupId})").FirstOrDefault("");
            playersInBracket.Add(steamIdToNickname(playerOneSteamId, database));
            string playerTwoSteamId = database.Query<string>($"SELECT [player_two_steamid] FROM [matches] WHERE (match_number = {i}) AND (cup_id = {cupId})").FirstOrDefault("");
            playersInBracket.Add(steamIdToNickname(playerTwoSteamId, database));

        }

        return playersInBracket;
}

static string profileTemplate(string profileName, string avatarUrl, int wincount) {
    return @$"
    <div class=""centre"">
        <h3>{profileName}</h3>
        <img src=""{avatarUrl}"">
        <p>Match wins: {wincount}</p>
    </div>";
}

static string headerProfileTemplate(string profileName, string avatarUrl) {
    return @$"
    <a class=""centre"" href=profile.html>
    <div id=""header_profile"">
        <img class=""centre"" id=""header_profile_image"" src=""{avatarUrl}"">
        <p class=""centre"" id=""header_profile_name"">{profileName}</p>
    </div>
    </a>";
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
        <p>Your opponent is:</p>
        <img src=""{opponentAvatar}"">
        <h3>{opponentName}</h3>
        <p>Please host a public lobby and wait for {opponentName} to join. You'll know it is actually them as they will type the codeword '{sharedword}' in the chat.</p>
        <br>
        <p>If you are unsure of the game settings to choose for the lobby you can find them <a href=""info.html"">here</a>.</p>
        <br>
        <div id=""result_submission"">
            <p>Once complete, submit the result below:</p>
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
        <p>Your opponent is:</p>
        <img src=""{opponentAvatar}"">
        <p>{opponentName}</p>
        <p> {opponentName} will host a public lobby. Please join it and type the codeword '{sharedword}' to confirm to them that you are the right person.</p>
        <br>
        <div id=""result_submission"">
            <p>Once complete, submit the result below:</p>
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

static string openCupTemplate(string API_URL, int currentOpenCupId) {
    return @$"
        <h3>Cup #{currentOpenCupId} is open for registration</h3>
        <div class=""centre"" id=""register"">

            <button
            hx-get=""{API_URL}/register""
            hx-target=""#register""
            hx-swap=""innerHTML""
            hx-trigger=""click"">
            Register for cup
            </button>

        </div>
    ";
}

static string bracketTemplate(List<string> playersInBracket, string bracketTitle, string cupStatus, string cupWinnerName, string cupWinnerAvatarUrl, IDbConnection database) {
    
    string response = @$"
    <div class=""centre bracket_wrapper"">
    <h3>{bracketTitle}</h3>
    <div class=""bracket"">
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
    </div>
    </div>
    ";
} else {
    response += "</div></div>";
}

return response;
}

app.Run();

public struct Match { 
    public Guid uuid;
    public string date;
    public int cup_id;
    public int match_number;
    public string status;
    public string player_one_steamid;
    public string player_two_steamid;
    public string player_one_declared_result;
    public string player_two_declared_result;
    public string winner_steamid;
    public string score;
    public string shared_word;
}

// TABLES I WILL NEED WITH WHAT DATA
// Players - SteamID, nickname, steam avatar, wincount
// Matches - UUID, date, cup_id, status(pending, complete) playerOneSteamId, playerTwoSteamID, winner(blank atm), score
// Cups - ID, date, number of players, winner
