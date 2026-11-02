-- PBZL3Sprint: only locomotion and the shared R3 gesture are overridden.
-- Circle dodge, R1 execution and Cross interaction are set explicitly for new users.
-- Other attacks, menus and keyboard bindings inherit the current SAV.
return {
    mode = "apply",
    active_controller = "dualsense",
    profiles = {
        xbox = { bindings = {}, trigger_parameters = {}, context_priority_offsets = {} },
        switch = { bindings = {}, trigger_parameters = {}, context_priority_offsets = {} },
        dualsense = {
            bindings = {
                IA_SprintSwitch = "L3",
                IA_Crouch = "R3",
                IA_LockTarget = "R3",
                IA_Dodge = "Circle", IA_DodgeHold = "Circle",
                IA_Execution = "R1", IA_Interaction = "Cross",
            },
            trigger_modes = {
                IA_Sprint = { mode = "off" },
                IA_SprintSwitch = { mode = "press" },
                IA_Crouch = { mode = "hold", time = 0.50 },
                IA_LockTarget = { mode = "tap", time = 0.50 },
            },
            trigger_parameters = {},
            context_priority_offsets = {},
        },
    },
}
