-- PBZL3Sprint 1.2.0. A distributable entry point for the locally supplied
-- PBZControllerCustomizer 2.1 native-input adapter by Sherkirito.
-- Does not load the editor, bind F7, save input settings or emulate buttons.
local dir = assert(debug.getinfo(1, "S").source:sub(2):match("^(.*[/\\])"))
local libraryDir = dir .. "../../../PBZL3SprintDependencies/PBZControllerCustomizer/Scripts/"
local function log(message) print("[PBZL3Sprint] " .. tostring(message) .. "\n") end
local function readFile(path)
    local f = assert(io.open(path, "rb"), "Missing module: " .. path)
    local text = f:read(1024 * 1024 + 1)
    f:close()
    assert(text and #text <= 1024 * 1024, "Invalid or oversized module: " .. path)
    return text
end
local function hash(text)
    local value = 2166136261
    for i = 1, #text do value = ((value ~ text:byte(i)) * 16777619) & 0xffffffff end
    return string.format("%08x", value)
end
local pinned = {
    ["catalog.lua"] = {159699, "716fa237"},
    ["logic.lua"] = {24342, "fd627137"},
    ["runtime.lua"] = {54512, "3d372d97"},
    ["sprint_recovery.lua"] = {7287, "b0baf49c"},
    ["compat.lua"] = {4197, "bbc0accb"},
}
local function checkPlan(plan)
    local keys = {
        IA_SprintSwitch = "Gamepad_LeftThumbstick",
        IA_Crouch = "Gamepad_RightThumbstick",
        IA_LockTarget = "Gamepad_RightThumbstick",
        IA_Dodge = "Gamepad_FaceButton_Right",
        IA_DodgeHold = "Gamepad_FaceButton_Right",
        IA_Execution = "Gamepad_RightShoulder",
        IA_Interaction = "Gamepad_FaceButton_Bottom",
    }
    local seen = {}
    assert(#plan.triggers == 0 and #plan.priorities == 0 and not plan.attack_guard,
        "Unexpected shared-trigger or priority override")
    for _,binding in ipairs(plan.bindings) do
        assert(keys[binding.action_name] == binding.key, "Unexpected binding: " .. binding.action_name)
        seen[binding.action_name] = true
    end
    for _,name in ipairs({"IA_SprintSwitch", "IA_Crouch", "IA_LockTarget"}) do
        assert(seen[name], "Missing binding: " .. name)
    end
    local modes = {IA_Sprint="off", IA_SprintSwitch="press", IA_Crouch="hold", IA_LockTarget="tap"}
    seen = {}
    for _,mode in ipairs(plan.modes) do
        assert(modes[mode.action_name] == mode.mode, "Unexpected trigger: " .. mode.action_name)
        if mode.action_name == "IA_Crouch" then
            assert(mode.oneShot and mode.value == 0.5, "Crouch must fire once after 0.5 seconds")
        elseif mode.action_name == "IA_LockTarget" then
            assert(math.abs(mode.value - 0.49999) < 0.000001, "R3 tap/hold boundary changed")
        end
        seen[mode.action_name] = true
    end
    for name in pairs(modes) do assert(seen[name], "Missing trigger: " .. name) end
end
local function start()
    assert(not rawget(_G, "PBZ_L3_SPRINT_RUNTIME"), "Already loaded; restart before reloading")
    assert(rawget(_G, "PBZ_CUSTOMIZER_HOT_CONFIG") == nil, "Another customizer is starting")
    for name, expected in pairs(pinned) do
        local text = readFile(libraryDir .. name)
        assert(#text == expected[1] and hash(text) == expected[2], "Mixed adapter version: " .. name)
    end
    local compat = dofile(libraryDir .. "compat.lua")
    local available, reason = compat.environment()
    assert(available, reason)
    local cfg, problem = compat.settings(readFile(dir .. "settings.lua"))
    assert(cfg, problem)
    assert(cfg.mode == "apply" and cfg.active_controller == "dualsense", "Unexpected configuration profile")
    local plan = dofile(libraryDir .. "logic.lua").compile(cfg, dofile(libraryDir .. "catalog.lua"))
    checkPlan(plan)
    -- Verify the native layout before the adapter can modify any mapping.
    local guard = dofile(dir .. "native_guard.lua")
    local build = guard.verify(libraryDir .. "../../../../PBZ.exe")
    local repair = guard.adapter(dofile(libraryDir .. "sprint_recovery.lua"))
    -- Give just this runtime its own dofile. Deferred callbacks keep this environment;
    -- other mods and the shared global dofile are never changed.
    local environment = setmetatable({dofile=function(path)
        if path == libraryDir .. "sprint_recovery.lua" then return repair end
        return dofile(path)
    end}, {__index=_G, __newindex=_G})
    local chunk = assert(load(readFile(libraryDir .. "runtime.lua"),
        "@" .. libraryDir .. "runtime.lua", "t", environment))
    PBZ_CUSTOMIZER_HOT_CONFIG = cfg
    local loaded, runtime = xpcall(chunk, debug.traceback)
    PBZ_CUSTOMIZER_HOT_CONFIG = nil
    assert(loaded, runtime)
    PBZ_L3_SPRINT_RUNTIME = runtime
    log("v1.2.0 ready; verified Steam build " .. build .. "; waiting for a player pawn.")
    log("L3=toggle sprint; R3 tap=lock, hold 0.50s=crouch. Existing settings and SAV preserved.")
end
local ok, reason = xpcall(start, debug.traceback)
if not ok then log("Startup stopped: " .. tostring(reason)) end
