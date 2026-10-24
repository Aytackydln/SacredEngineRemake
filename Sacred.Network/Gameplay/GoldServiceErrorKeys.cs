namespace Sacred.Network.Gameplay;

/// <summary>Original 802080 error-to-resource-key table. Unknown codes leave its empty string unchanged.</summary>
public static class GoldServiceErrorKeys
{
    public static string? Get(uint code) => code switch
    {
        1 => "UI_ERROR_WRONG_VERSION2",
        2 => "UI_ERROR_DENY_PRIVINFO_CHANGE",
        4 => "UI_ERROR_DENY_PUBINFO_CHANGE",
        6 => "UI_ERROR_CDKEY_WRONG",
        7 => "UI_CDKEY_ADDON_INVALID",
        8 => "UI_ERROR_PWD",
        9 => "UI_ERROR_UNKNOWN_USER",
        10 => "UI_ERROR_USER_EXISTS",
        11 => "UI_ERROR_ALREADY_ONLINE",
        12 => "UI_ERROR_DENY_PUBDATA_CHANGE",
        14 => "UI_ERROR_PUBDATA_INVALID_BLOCK",
        15 => "UI_ERROR_INTERNAL_ERROR",
        16 => "UI_ERROR_GAME_LOGIN_DENIED",
        17 => "UI_ERROR_GAME_UPDATE_DENIED",
        18 => "UI_ERROR_GAME_REMOVE_DENIED",
        19 => "UI_ERROR_SELECT_DATASET_DENIED",
        20 => "UI_ERROR_JOIN_CHANNEL_DENIED",
        21 => "UI_ERROR_SERVER_LOSTLOBBY",
        22 => "UI_ERROR_SERVER_RECONNECT2LOBBY",
        23 => "UI_ERROR_LOBBY_FULL",
        24 => "ERROR_LOBBY_ACCOUNT_NOT_ACTIVE",
        25 => "ERROR_LOBBY_CDKEY_BOUND",
        26 => "ERROR_LOBBY_CDKEY_UW_BOUND",
        27 => "UI_ERROR_INTERNAL_ERROR",
        28 => "UI_ERROR_INTERNAL_ERROR",
        29 => "UI_ERROR_INTERNAL_ERROR",
        30 => "UI_ERROR_INTERNAL_ERROR",
        31 => "UI_ERROR_INTERNAL_ERROR",
        32 => "ERROR_LOBBY_USERNOTFOUND",
        33 => "ERROR_LOBBY_USERNOTFOUND",
        34 => "ERROR_LOBBY_USERBANNED",
        35 => "ERROR_LOBBY_BACKCONNECT_FAILED",
        36 => "ERROR_LOBBY_NOT_A_GAME",
        100 => "UI_ERROR_NOTALLOWED",
        101 => "UI_ERROR_PLAYERKICK",
        102 => "UI_ERROR_PLAYER_NOTFOUND",
        103 => "UI_ERROR_PWD",
        104 => "UI_ERROR_HOST_SHUTDOWN",
        105 => "UI_ERROR_HOST_TRANSFER",
        106 => "UI_ERROR_PWD_TIMEOUT",
        107 => "UI_ERROR_CHEATERKICK",
        108 => "UI_NET_CHAR_CORRUPTED_LONG",
        109 => "UI_NET_IDLE_WARNING",
        _ => null
    };
}
