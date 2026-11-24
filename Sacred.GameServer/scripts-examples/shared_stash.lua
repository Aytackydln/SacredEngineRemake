-- Shared stash for Closed Net: the stash of the Sacred Gear mod shared-stash (sacred-gear-mods src/shared-stash) kept by the game
-- server, per lobby account, so a player cannot edit a file or restore a save to duplicate stashed items.
-- Copy into the server's scripts folder and run the server with --authoritative (the server then writes the heroes to the lobby).
--
-- Channel "shared-stash" (server_only). Messages (JSON tables, `op`):
--   client: info | list | deposit {rid, ref, data, name, quality, w, h} | withdraw {rid, id} | collected {rid, ref} | abort {rid}
--   server: info {closed, count, max} | entry {id, data, name, quality, w, h, t} ... listed {count} | result {rid, ok, err, id, count}
--           release {rid, id, data} (withdraw reserved: create the item, then send `collected`) | collected {rid, ok, retry, err}
--
-- A deposit counts only after the server has seen the item leave the hero (destroyed by its owner, the same item data) and the
-- lobby has stored the hero without it; a withdrawal's entry is only removed after the server has seen the new item arrive in the
-- hero and the lobby has stored the hero with it. Every change is written durably (server.flush) before its answer is sent; a
-- failed write is undone and refused. Requests carry ids (rid): a repeated request gets the same answer and changes nothing.
-- Outside Closed Net (not authoritative: LAN, Open Net, a plain relay) this script offers nothing: `info` says closed = false and
-- every other request is refused; the mod keeps its own local stash there, but never in Closed Net.
--
-- Per account, in this mod's storage (script-data/shared_stash.json, key "acct.<account>"): the entries and the answers by rid.
-- Crash handling, entry states: `pending` (item seen leaving, hero save not confirmed yet) is made ready once a hero upload went
-- out since, and dropped when the player joined again without one (the lobby's hero still has the item); `reserved` (taken out
-- for a withdrawal) is removed when a hero upload since shows the item in the hero, and released again when none went out.

server.describe("Shared stash (Closed Net): per-account item stash kept by the server for the Sacred Gear mod shared-stash.")

local config = {
    max_items = 200,            -- per account
    max_data = 16384,           -- hex characters of one item's data
    verify_seconds = 30,        -- how long to wait for the server to see an item leave or arrive
    retry_seconds = 15,         -- a deposit whose hero save failed is saved again this often
    rid_days = 14,
    rids_max = 300,
    token_seconds = 300,        -- how long the server's permission for the client to create a withdrawn item lives
    rate_per_minute = 30,       -- deposits + withdrawals per account per minute (0: no limit)
}
-- Tunables are data: the table above only holds the first-run defaults. They are written to this mod's storage under "config" the first
-- time and from then on the storage wins (edit them on this mod's page /mods/shared_stash/, or POST /api/mod-store with {"mod":"shared_stash",
-- "set":{"config":{...}}}); the key config_rev (bumped by that POST) is polled every second, so a change applies without a reload.
local DEFAULTS = {}
for key, value in pairs(config) do DEFAULTS[key] = value end

local function apply_config()
    local o = server.load("config")
    if type(o) ~= "table" then return end
    for key in pairs(DEFAULTS) do
        local v = o[key]
        if type(v) == "number" and v == v and v >= 0 then config[key] = v else config[key] = DEFAULTS[key] end
    end
end
if server.load("config") == nil then server.save("config", DEFAULTS) end
local config_rev = tonumber(server.load("config_rev")) or 0
apply_config()
server.every(1, function()
    local rev = tonumber(server.load("config_rev")) or 0
    if rev ~= config_rev then config_rev = rev apply_config() end
end)

local CHANNEL = "shared-stash"
local ch = net.channel(CHANNEL, { server_only = true })

local accounts = {}         -- account -> state (loaded on demand)
local sessions = {}         -- account -> server time the account joined (this run of the server)
local pending = {}          -- account .. " " .. rid -> a request waiting for the server to see something (not stored: clients resend)
local rate = {}             -- account -> { minute, count }

local function copy(v)
    if type(v) ~= "table" then return v end
    local t = {}
    for k, x in pairs(v) do t[k] = copy(x) end
    return t
end

local function int(v)
    if type(v) ~= "number" or v ~= v then return nil end
    return math.floor(v)
end

-- gear.inv.export: u32 type, u16 n, n entries of 130 bytes (the socketed runes), then the item's own 130-byte record, ...: the value
-- records as the server keeps them (hero_item.records) are those n + 1 blocks
local function entries_hex(data)
    if type(data) ~= "string" or #data < 12 then return nil end
    local lo, hi = tonumber(data:sub(9, 10), 16), tonumber(data:sub(11, 12), 16)
    if not lo or not hi then return nil end
    local count = lo + hi * 256
    if count > 8 or #data < 12 + (count + 1) * 260 then return nil end
    return data:sub(13, 12 + (count + 1) * 260)
end

local function type_of(data)
    if type(data) ~= "string" or #data < 8 then return nil end
    local b = {}
    for i = 1, 4 do b[i] = tonumber(data:sub(i * 2 - 1, i * 2), 16) end
    if not (b[1] and b[2] and b[3] and b[4]) then return nil end
    return b[1] + b[2] * 256 + b[3] * 65536 + b[4] * 16777216
end

local function valid_hex(s)
    return type(s) == "string" and #s >= 12 and #s <= config.max_data and #s % 2 == 0 and not s:find("[^0-9a-fA-F]")
end

local function known_type(data)
    local ty = type_of(data)
    return ty ~= nil and ty > 0 and ty < 0x10000
end

local function in_hero(where) return where == "backpack" or where == "stash" or where == "equipped" end

local function text(v, max)
    if type(v) ~= "string" then return nil end
    return v:sub(1, max)
end

-- ---- state ------------------------------------------------------------------------------------------------------------------
local function load_account(account)
    local s = accounts[account]
    if s then return s end
    s = server.load("acct." .. account) or {}
    s.items = s.items or {}
    s.rids = s.rids or {}
    s.next = s.next or 1
    accounts[account] = s
    return s
end

-- A small index for the admin page: account -> counts (opaque keys only).
local function summary(s)
    local n = { ready = 0, reserved = 0, pending = 0 }
    for _, e in ipairs(s.items) do n[e.state] = (n[e.state] or 0) + 1 end
    return n
end

local index = server.load("index")
if type(index) ~= "table" then index = {} end

-- Writes the account's state durably; done(ok, err). On failure the caller's undo has the old state.
local function write(account, done)
    local s = accounts[account]
    index[account] = summary(s)
    pcall(server.save, "index", index)
    local ok, err = pcall(server.save, "acct." .. account, s)
    if not ok then return done(false, tostring(err)) end
    server.flush(function(written, why) done(written and true or false, why) end)
end

-- Changes `s` with fn(s), writes, and calls done(ok, err); a failed write puts the old state back (and stores it again).
local function change(account, fn, done)
    local s = load_account(account)
    local before = copy(s)
    fn(s)
    write(account, function(ok, err)
        if not ok then
            accounts[account] = before
            pcall(server.save, "acct." .. account, before)
            server.log("shared stash: a write failed and was undone: " .. tostring(err))
        end
        done(ok, err)
    end)
end

local function prune_rids(s, now)
    local n, oldest = 0, nil
    for rid, r in pairs(s.rids) do
        n = n + 1
        if (r.t or 0) < now - config.rid_days * 86400 then s.rids[rid] = nil n = n - 1 end
    end
    if n > config.rids_max then
        local list = {}
        for rid, r in pairs(s.rids) do list[#list + 1] = { rid = rid, t = r.t or 0 } end
        table.sort(list, function(a, b) return a.t < b.t end)
        for i = 1, n - config.rids_max do s.rids[list[i].rid] = nil end
    end
end

-- The join times survive a reload of this script (crash recovery tells "this session" from "an earlier one" by them): written by the
-- unload hook when the reason is a reload, taken (and deleted) by the next load. After a restart or a crash there are none.
local function save_sessions() end

local function allowed_rate(account)
    if config.rate_per_minute <= 0 then return true end
    local minute = math.floor(server.time() / 60)
    local r = rate[account]
    if not r or r.minute ~= minute then r = { minute = minute, count = 0 } rate[account] = r end
    r.count = r.count + 1
    return r.count <= config.rate_per_minute
end

local function find(s, id)
    for i, e in ipairs(s.items) do if e.id == id then return e, i end end
end

local function ready_count(s)
    local n = 0
    for _, e in ipairs(s.items) do if e.state == "ready" or e.state == "reserved" then n = n + 1 end end
    return n
end

local function send(slot, msg)
    if ch and net.supports(slot) then ch:send(slot, msg) end
end

local function result(slot, rid, ok, err, extra)
    local m = { op = "result", rid = rid, ok = ok and true or false, err = err }
    if extra then for k, v in pairs(extra) do m[k] = v end end
    send(slot, m)
end

local function record(s, rid, reply)
    reply.t = math.floor(server.time())
    s.rids[rid] = reply
    prune_rids(s, reply.t)
end

local function entry_message(e)
    return { op = "entry", id = e.id, data = e.data, name = e.name, quality = e.quality, w = e.w, h = e.h, t = e.t,
        reserved = e.state == "reserved" or nil }
end

local function current_player(account)
    for _, p in ipairs(server.players()) do if p.account == account then return p end end
end

-- ---- crash recovery -------------------------------------------------------------------------------------------------------
-- Entries left in a middle state by an earlier session of this account (a crash, a lost connection) or by a failed hero save.
local function reconcile(player, account)
    local s = load_account(account)
    local joined = sessions[account] or 0
    local uploaded = server.hero_upload_time(player)
    local changed = false
    local keep = {}
    local items
    for _, e in ipairs(s.items) do
        local drop = false
        local busy = false
        for key in pairs(pending) do
            if key:sub(1, #account + 1) == account .. " " and (pending[key].entry == e.id) then busy = true end
        end
        if busy then
            -- a request is on it
        elseif e.state == "pending" and (e.t or 0) < joined then
            if uploaded and uploaded >= (e.t or 0) then e.state = "ready" changed = true
            else drop = true changed = true end
        elseif e.state == "reserved" and (e.rt or 0) < joined then
            if uploaded and uploaded >= (e.rt or 0) then
                items = items or server.hero_items(player)
                -- the saved hero has more items with these records than at the reservation: the withdrawn one is in it
                local want, n = entries_hex(e.data), 0
                for _, it in ipairs(items) do
                    if it.records == want then n = n + 1 end
                end
                if n > (e.pre or 0) then drop = true end
                if not drop then e.state = "ready" e.rid = nil e.rt = nil end
            else
                e.state = "ready" e.rid = nil e.rt = nil
            end
            changed = true
        end
        if not drop then keep[#keep + 1] = e end
    end
    if changed then
        s.items = keep
        write(account, function(ok, err)
            if not ok then server.log("shared stash: recovery could not be stored: " .. tostring(err)) end
        end)
    end
end

-- ---- requests -------------------------------------------------------------------------------------------------------------
local function after_save(player, account, e, on_saved, on_failed)
    e.stage = "saving"
    server.save_hero(player, function(ok, err)
        if pending[e.key] ~= e then return end
        e.stage = nil
        if ok then pending[e.key] = nil on_saved() else on_failed(err) end
    end)
end

local CHECKS = {}

-- deposit: the item (msg.ref) left the hero, the same data; then the entry exists as `pending`, the hero is saved, the entry is ready.
function CHECKS.deposit(e, player, now)
    local msg, account = e.msg, e.account
    local s = load_account(account)
    local function finish()
        change(account, function(st)
            local entry = find(st, e.entry)
            if entry then entry.state = "ready" end
            record(st, msg.rid, { ok = true, id = e.entry })
        end, function(ok, err)
            if not ok then
                pending[e.key] = nil
                return result(player.slot, msg.rid, false, "the stash could not be written (" .. tostring(err) .. "); try again")
            end
            result(player.slot, msg.rid, true, nil, { id = e.entry, count = ready_count(load_account(account)) })
        end)
    end
    local function save()
        after_save(player, account, e, finish, function(err)
            -- the entry stays `pending`; the item is in the stash once a save goes through (sent again below)
            e.retry_at = server.time() + config.retry_seconds
            send(player.slot, { op = "result", rid = msg.rid, ok = false, retry = true,
                err = "your hero could not be saved (" .. tostring(err) .. "); trying again" })
        end)
    end
    if e.entry then
        if e.stage == "saving" then return end
        if e.retry_at and now < e.retry_at then return end
        return save()
    end
    local item = server.hero_item(player, msg.ref)
    if item and item.where == "gone" then
        local entry = { id = s.next, state = "pending", t = math.floor(now), data = msg.data, name = text(msg.name, 80) or "?",
            quality = text(msg.quality, 16), w = int(msg.w) or 1, h = int(msg.h) or 1, rid = msg.rid }
        if item.type ~= type_of(msg.data) or item.records ~= entries_hex(msg.data) then
            pending[e.key] = nil
            server.log("shared stash: " .. player.name .. ": a deposit whose data is not the item that left the hero (ref " .. tostring(msg.ref) .. ")")
            return result(player.slot, msg.rid, false, "the item data does not match the item that left your inventory")
        end
        if ready_count(s) >= config.max_items then
            pending[e.key] = nil
            return result(player.slot, msg.rid, false, "the stash is full (" .. config.max_items .. " items); the item stays gone, rejoin to get it back")
        end
        if not server.claim_item(player, msg.ref) then
            pending[e.key] = nil
            return result(player.slot, msg.rid, false, "that item was stashed already")
        end
        e.entry = entry.id
        server.log("shared stash: " .. player.name .. ": deposit " .. msg.rid .. " -> entry " .. entry.id .. " (type " .. tostring(type_of(msg.data)) .. ")")
        e.stage = "writing"
        return change(account, function(st) st.next = st.next + 1 st.items[#st.items + 1] = entry end, function(ok, err)
            e.stage = nil
            if not ok then
                pending[e.key] = nil
                return result(player.slot, msg.rid, false, "the stash could not be written (" .. tostring(err) .. "); rejoin to get the item back")
            end
            save()
        end)
    end
    if now < e.deadline then return end
    pending[e.key] = nil
    if item and item.where ~= "gone" then
        return result(player.slot, msg.rid, false, "the server still sees the item (" .. item.where .. "); it is yours there", { keep = true })
    end
    result(player.slot, msg.rid, false, "the server did not see the item leave your inventory")
end

-- collected: the withdrawn item (msg.ref) is in the hero with the entry's records; the hero is saved; the entry goes.
function CHECKS.collected(e, player, now)
    local msg, account = e.msg, e.account
    local s = load_account(account)
    local entry = find(s, e.entry)
    if not entry then pending[e.key] = nil return end
    local item = type(msg.ref) == "number" and server.hero_item(player, msg.ref)
    local old = false
    for _, ref in ipairs(entry.pre_refs or {}) do if ref == msg.ref then old = true end end
    if old then
        pending[e.key] = nil
        server.log("shared stash: " .. player.name .. ": withdrawal " .. msg.rid .. " claims an item that was in the hero before it")
        return send(player.slot, { op = "collected", rid = msg.rid, ok = false, err = "that item was in your inventory before the withdrawal" })
    end
    if item and in_hero(item.where) and item.type == type_of(entry.data) and item.records == entries_hex(entry.data) then
        if not server.claim_item(player, msg.ref) then
            server.log("shared stash: " .. player.name .. ": withdrawal " .. msg.rid .. ": no permission token left for the arrived item (expired?), accepted")
        end
        return after_save(player, account, e, function()
            change(account, function(st)
                local _, i = find(st, e.entry)
                if i then table.remove(st.items, i) end
                record(st, msg.rid, { ok = true, collected = true })
            end, function(ok, err)
                if not ok then
                    return send(player.slot, { op = "collected", rid = msg.rid, ok = false, retry = true, err = "the stash could not be written; trying again" })
                end
                send(player.slot, { op = "collected", rid = msg.rid, ok = true, count = ready_count(load_account(account)) })
            end)
        end, function(err)
            send(player.slot, { op = "collected", rid = msg.rid, ok = false, retry = true, err = "your hero could not be saved: " .. tostring(err) })
        end)
    end
    if now < e.deadline then return end
    pending[e.key] = nil
    send(player.slot, { op = "collected", rid = msg.rid, ok = false, retry = true, err = "the server has not seen the item arrive yet" })
end

local function handle(player, msg)
    local account = player.account
    local op = msg.op
    if not server.authoritative(player) then
        -- no custody here: say so, never store anything
        return send(player.slot, { op = "info", closed = false, reason = "the server does not run in authoritative Closed Net mode" })
    end
    if not account then
        return send(player.slot, { op = "info", closed = false, reason = "no lobby account" })
    end
    local s = load_account(account)
    if op == "info" or op == "list" then
        reconcile(player, account)
        s = load_account(account)
        send(player.slot, { op = "info", closed = true, where = "server", count = ready_count(s), max = config.max_items })
        if op == "list" then
            local n = 0
            for _, e in ipairs(s.items) do
                if e.state == "ready" then send(player.slot, entry_message(e)) n = n + 1 end
            end
            send(player.slot, { op = "listed", count = n })
        end
        return
    end
    local rid = msg.rid
    if op == "abort" then
        if type(rid) ~= "string" then return end
        for _, e in ipairs(s.items) do
            if e.state == "reserved" and e.rid == rid then
                -- the client made no item: give the entry back (unless the item is in the hero after all)
                local key = account .. " " .. rid
                pending[key] = nil
                return change(account, function(st)
                    local entry = find(st, e.id)
                    if entry then entry.state = "ready" entry.rid = nil entry.rt = nil end
                end, function(ok) result(player.slot, rid, ok, ok and nil or "the stash could not be written", { aborted = true }) end)
            end
        end
        return result(player.slot, rid, true, nil, { aborted = true })
    end
    if type(rid) ~= "string" or #rid < 4 or #rid > 64 then return result(player.slot, nil, false, "a request id is needed") end
    local key = account .. " " .. rid
    local done = s.rids[rid]
    if done and (op == "deposit" or op == "withdraw") then
        return send(player.slot, { op = "result", rid = rid, ok = done.ok, err = done.err, id = done.id, data = done.data, again = true,
            count = ready_count(s) })
    end
    if op == "collected" and done and done.collected then
        return send(player.slot, { op = "collected", rid = rid, ok = true, count = ready_count(s) })
    end
    local now = server.time()
    if (op == "deposit" or op == "withdraw") and not pending[key] and not allowed_rate(account) then
        return result(player.slot, rid, false, "too many stash operations, wait a moment")
    end
    if op == "deposit" then
        if pending[key] then return end
        if type(msg.ref) ~= "number" then return result(player.slot, rid, false, "the item's id is needed") end
        if not valid_hex(msg.data) or not entries_hex(msg.data) or not known_type(msg.data) then
            local why = not valid_hex(msg.data) and ("not hex or out of size (" .. #tostring(msg.data) .. " characters, limit " .. config.max_data .. ")")
                or not entries_hex(msg.data) and "no value records" or "unknown item type"
            server.log("shared stash: " .. player.name .. ": a deposit with invalid item data was refused: " .. why)
            return result(player.slot, rid, false, "the item data is not valid (" .. why .. ")")
        end
        -- a pending entry of this rid (earlier hero save failed): the request resumes there
        local resume
        for _, e in ipairs(s.items) do if e.state == "pending" and e.rid == rid then resume = e.id end end
        pending[key] = { key = key, kind = "deposit", account = account, msg = msg, deadline = now + config.verify_seconds, entry = resume }
        return CHECKS.deposit(pending[key], player, now)
    elseif op == "withdraw" then
        if pending[key] then return end
        local id = int(msg.id)
        local entry = id and find(s, id)
        if not entry or (entry.state ~= "ready" and not (entry.state == "reserved" and entry.rid == rid)) then
            return result(player.slot, rid, false, "that item is not in the stash (any more)")
        end
        if entry.state == "reserved" then
            return send(player.slot, { op = "release", rid = rid, id = entry.id, data = entry.data })
        end
        local data = entry.data
        -- the items of the hero that already look like it: the arrival must be another one (by id), and counted at a restart
        local want, pre_refs = entries_hex(data), {}
        for _, it in ipairs(server.hero_items(player)) do
            if it.records == want then pre_refs[#pre_refs + 1] = it.ref end
        end
        return change(account, function(st)
            local e = find(st, id)
            e.state = "reserved" e.rid = rid e.rt = math.floor(now) e.pre = #pre_refs e.pre_refs = pre_refs
        end, function(ok, err)
            if not ok then return result(player.slot, rid, false, "the stash could not be written (" .. tostring(err) .. ")") end
            -- the server's permission for the client to create exactly this item (see --auth-strict-items); consumed by claim_item
            local token = server.item_token(player, { data = data, label = "shared-stash" }, config.token_seconds)
            server.log("shared stash: " .. player.name .. ": withdrawal " .. rid .. " of entry " .. id .. " released" .. (token and (" (token " .. token .. ")") or ""))
            send(player.slot, { op = "release", rid = rid, id = id, data = data })
        end)
    elseif op == "collected" then
        if pending[key] and pending[key].kind == "collected" and pending[key].stage == "saving" then return end
        local entry
        for _, e in ipairs(s.items) do if e.state == "reserved" and e.rid == rid then entry = e end end
        if not entry then return send(player.slot, { op = "collected", rid = rid, ok = false, err = "nothing is reserved for that request" }) end
        pending[key] = { key = key, kind = "collected", account = account, msg = msg, entry = entry.id, deadline = now + config.verify_seconds }
        return CHECKS.collected(pending[key], player, now)
    end
end

if ch then
    ch:on(function(player, msg)
        if type(msg) ~= "table" or type(msg.op) ~= "string" then return end
        handle(player, msg)
    end)
end

server.every(0.5, function()
    local now = server.time()
    for key, e in pairs(pending) do
        local player = current_player(e.account)
        if not player then
            pending[key] = nil
        elseif e.stage ~= "saving" and e.stage ~= "writing" then
            CHECKS[e.kind](e, player, now)
        end
    end
end)

server.on("join", function(player)
    if player.account then sessions[player.account] = math.floor(server.time()) save_sessions() end
end)
server.on("leave", function(player)
    local account = player.account
    if not account then return end
    sessions[account] = nil
    save_sessions()
    for key in pairs(pending) do if key:sub(1, #account + 1) == account .. " " then pending[key] = nil end end
end)
-- (re)loaded while players are in: they keep the join time stored before (a reload is not a new session)
do
    local saved = server.load("reload_sessions")
    if saved ~= nil then server.delete("reload_sessions") end
    if type(saved) ~= "table" then saved = {} end
    for _, p in ipairs(server.players()) do
        if p.account and not sessions[p.account] then sessions[p.account] = saved[p.account] or math.floor(server.time()) end
    end
end

-- Hot reload / shutdown: everything is stored at every change already (entries, pending and reserved states, answers by rid); the join
-- times and any write still in flight are made durable here. Tokens live in the server (C#) and survive a reload; in-flight checks are
-- resumed by the clients' resends (same rid).
server.on("unload", function(reason)
    if reason == "reload" then pcall(server.save, "reload_sessions", sessions) end
    server.flush(function() end)
end)

-- ---- admin page (the Mods tab of the web UI links it): counts, in-flight requests, the tunables ---------------------------------------
local function page_state()
    local totals = { accounts = 0, ready = 0, reserved = 0, pending = 0 }
    for _, n in pairs(index) do
        totals.accounts = totals.accounts + 1
        totals.ready = totals.ready + (n.ready or 0)
        totals.reserved = totals.reserved + (n.reserved or 0)
        totals.pending = totals.pending + (n.pending or 0)
    end
    local waiting = 0
    for _ in pairs(pending) do waiting = waiting + 1 end
    return { totals = totals, requests_in_flight = waiting, config = config, config_rev = config_rev, defaults = DEFAULTS }
end

server.endpoint("state", page_state, { description = "stash totals (opaque account keys are never listed), requests in flight, the tunables" })

server.page("", function()
    local s = page_state()
    local rows = {}
    for key, value in pairs(s.config) do
        rows[#rows + 1] = '<tr><td>' .. key .. '</td><td><input name="' .. key .. '" value="' .. tostring(value) .. '" size="8"></td></tr>'
    end
    table.sort(rows)
    return '<!doctype html><html lang="en"><head><meta charset="utf-8"><title>Shared stash</title><style>body{font:14px system-ui;margin:2em;max-width:40em}'
        .. 'td{padding:2px 8px}</style></head><body><h1>Shared stash (Closed Net)</h1><p>Accounts with entries: ' .. s.totals.accounts
        .. '. Items: ' .. s.totals.ready .. ' ready, ' .. s.totals.reserved .. ' reserved (withdrawals), ' .. s.totals.pending
        .. ' pending (deposits). Requests in flight: ' .. s.requests_in_flight .. '.</p><h2>Tunables</h2><form id="f"><table>'
        .. table.concat(rows) .. '</table><button>Save</button> <span id="m"></span></form><script>'
        .. 'document.getElementById("f").onsubmit=async e=>{e.preventDefault();const c={};for(const i of e.target.querySelectorAll("input"))c[i.name]=Number(i.value);'
        .. 'const r=await fetch("/api/mod-store",{method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify({mod:"shared_stash",set:{config:c}})});'
        .. 'document.getElementById("m").textContent=r.ok?"saved, applies within a second":"failed: "+r.status};</script></body></html>'
end, { title = "Shared stash" })
