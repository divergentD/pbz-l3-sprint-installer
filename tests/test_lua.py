"""Offline regression tests. Requires Python 3 and lupa==2.6; no game writes.

Usage: python tests/test_lua.py PATH_TO_ORIGINAL_2_1_SCRIPTS [PATH_TO_PBZ_EXE]
The restricted third-party adapter is supplied locally and is never bundled here.
"""
from pathlib import Path
import hashlib
import json
import re
import shutil
import sys
import tempfile
from lupa.lua54 import LuaRuntime

REPO = Path(__file__).resolve().parents[1]
REFERENCE = Path(sys.argv[1]).resolve()
ACTUAL_EXE = Path(sys.argv[2]).resolve() if len(sys.argv) > 2 else None
CHECKS = []


def check(condition, label):
    assert condition, label
    CHECKS.append(label)


def vm(scripts, library):
    lua = LuaRuntime(unpack_returned_tuples=True)
    lua.globals().script_dir = scripts.as_posix() + '/'
    lua.globals().library_dir = library.as_posix() + '/'
    lua.execute(r'''
        messages={}; print=function(s)messages[#messages+1]=tostring(s)end
        for _,name in ipairs({'StaticFindObject','StaticConstructObject','FName','FText',
          'RegisterKeyBind','RegisterHook','RegisterLoadMapPreHook','ExecuteInGameThread',
          'ExecuteInGameThreadWithDelay','CancelDelayedAction'})do _G[name]=function()end end
        loops=0
        LoopInGameThreadWithDelay=function(delay, callback) loops=loops+1; return loops end
        Key={F7=1}; EObjectFlags={RF_Transient=64}
        PropertyTypes={ByteProperty=1}
        package.preload.UEHelpers=function()return{
          GetPlayerController=function()end, GetGameInstance=function()end}end
        original_dofile=dofile
    ''')
    return lua


manifest = (REPO / 'payload/native_guard.lua').read_text('utf-8')
profiles = re.findall(r'\[(\d+)\] = \{build="(\d+)", spans=\{(.*?)\}\},', manifest, re.S)
check(len(profiles) == 2, 'two explicit native profiles; unknown builds have no fallback')

with tempfile.TemporaryDirectory(prefix='pbz-lua-tests-') as temp:
    game = Path(temp)
    scripts = game / 'platform/steam/ue4ss/Mods/PBZL3Sprint/Scripts'
    library = game / 'platform/steam/ue4ss/PBZL3SprintDependencies/PBZControllerCustomizer/Scripts'
    scripts.mkdir(parents=True)
    library.mkdir(parents=True)
    for src in (REPO / 'payload').glob('*.lua'):
        shutil.copy2(src, scripts / src.name)
    for name in ('catalog.lua', 'logic.lua', 'runtime.lua', 'compat.lua', 'sprint_recovery.lua'):
        shutil.copy2(REFERENCE / name, library / name)
    original_hashes = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in library.glob('*.lua')}
    exe = game / 'platform/steam/PBZ.exe'
    for size, build, spans in profiles:
        blocks = [(int(offset), bytes.fromhex(data)) for offset, data in
                  re.findall(r'\{(\d+),"([0-9a-f]+)"\}', spans)]
        with exe.open('wb') as f:
            f.truncate(int(size))
            for offset, block in blocks:
                f.seek(offset)
                f.write(block)
        lua = vm(scripts, library)
        for source in scripts.glob('*.lua'):
            assert not isinstance(lua.eval('loadfile')(source.as_posix()), tuple), source.name
        check(True, f'all distributed Lua files compile for build {build}')
        lua.globals().exe_path = exe.as_posix()
        check(lua.eval('dofile(script_dir.."native_guard.lua").verify(exe_path)') == build,
              f'Lua checks all four exact native spans for build {build}')
        lua.execute(r'''
            dofile(script_dir..'main.lua')
            assert(PBZ_L3_SPRINT_RUNTIME and PBZ_CUSTOMIZER_HOT_CONFIG==nil,table.concat(messages,'\n'))
            assert(dofile==original_dofile and loops==1)
            local status=PBZ_L3_SPRINT_RUNTIME.status()
            assert(status.planned==7 and status.modes==5 and status.failed==nil)
        ''')
        check(True, f'real pinned runtime boots with seven intended actions on build {build}')
        lua.execute(r'''
            local environment
            for index=1,20 do
                local name,value=debug.getupvalue(PBZ_L3_SPRINT_RUNTIME.tick,index)
                if name=='_ENV'then environment=value;break end
            end
            assert(environment and environment~=_G and environment.dofile~=dofile)
            local runtime_dir=debug.getinfo(PBZ_L3_SPRINT_RUNTIME.tick,'S').source:sub(2):match('^(.*[/\\])')
            local repair=environment.dofile(runtime_dir..'sprint_recovery.lua')
            assert(repair.verify(exe_path))
            StaticFindObject=function(path)
                assert(path=='/Script/SGameLocomotionDriver.GA_LocomotionDriver_3DDefault_Base')
                return{IsValid=function()return true end}
            end
            local registered
            RegisterCustomProperty=function(property)registered=property end
            local instance=repair.new(library_dir,function()end)
            assert(instance.alive and registered.OffsetInternal==0x4c6 and loops==2)
            instance.stop()
            assert(dofile==original_dofile and PBZ_CUSTOMIZER_HOT_CONFIG==nil)
        ''')
        check(True, f'deferred original recovery receives the new verifier and verified 0x4c6 field on build {build}')
        # A matching file size must never be sufficient. Check every native span independently.
        for offset, block in blocks:
            with exe.open('r+b') as f:
                f.seek(offset)
                f.write(bytes([block[0] ^ 0xff]))
            bad = vm(scripts, library)
            bad.execute(r'''
                dofile(script_dir..'main.lua')
                assert(PBZ_L3_SPRINT_RUNTIME==nil and PBZ_CUSTOMIZER_HOT_CONFIG==nil and loops==0)
                assert(table.concat(messages,'\n'):find('Native sprint layout differs',1,true))
            ''')
            with exe.open('r+b') as f:
                f.seek(offset)
                f.write(block)
        check(True, f'altering any native span stops before runtime or mapping changes on build {build}')

    # Preserve the personal v1.0 profile, which intentionally inherits combat keys from SAV.
    settings_path = scripts / 'settings.lua'
    default_settings = settings_path.read_bytes()
    legacy = default_settings.decode('utf-8')
    legacy = re.sub(r'^\s*IA_Dodge =.*\n', '', legacy, flags=re.M)
    legacy = re.sub(r'^\s*IA_Execution =.*\n', '', legacy, flags=re.M)
    settings_path.write_text(legacy, 'utf-8')
    lua = vm(scripts, library)
    lua.execute(r'''
        dofile(script_dir..'main.lua')
        assert(PBZ_L3_SPRINT_RUNTIME,table.concat(messages,'\n'))
        assert(PBZ_L3_SPRINT_RUNTIME.status().planned==3 and PBZ_CUSTOMIZER_HOT_CONFIG==nil)
    ''')
    check(True, 'legacy three-action configuration boots while inheriting all combat bindings')
    settings_path.write_bytes(default_settings)

    lua = vm(scripts, library)
    lua.execute(r'''
        LoopInGameThreadWithDelay=function()error('injected initialization failure')end
        dofile(script_dir..'main.lua')
        assert(PBZ_L3_SPRINT_RUNTIME==nil and PBZ_CUSTOMIZER_HOT_CONFIG==nil)
        assert(dofile==original_dofile)
    ''')
    check(True, 'runtime initialization failure clears handoff and preserves shared globals')
    lua = vm(scripts, library)
    lua.execute(r'''
        local cfg=dofile(script_dir..'settings.lua')
        local plan=dofile(library_dir..'logic.lua').compile(cfg,dofile(library_dir..'catalog.lua'))
        local seen={}
        for _,mode in ipairs(plan.modes)do
            seen[mode.action_name]=true
            if mode.action_name=='IA_Crouch'then assert(mode.oneShot and mode.value==0.5)end
            if mode.action_name=='IA_LockTarget'then assert(math.abs(mode.value-0.49999)<0.000001)end
        end
        assert(seen.IA_Crouch and seen.IA_LockTarget and seen.IA_Sprint and seen.IA_SprintSwitch)
    ''')
    check(True, 'R3 hold/tap retain the non-overlapping 0.5 second boundary')
    with exe.open('r+b') as f:
        f.truncate(12345)
    lua = vm(scripts, library)
    lua.execute(r'''
        dofile(script_dir..'main.lua')
        assert(PBZ_L3_SPRINT_RUNTIME==nil and loops==0)
        assert(table.concat(messages,'\n'):find('Unsupported game executable',1,true))
    ''')
    check(True, 'unknown build stops before input changes')
    check(original_hashes == {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in library.glob('*.lua')},
          'all five restricted dependency files remain byte-for-byte unchanged')
    if ACTUAL_EXE:
        lua.globals().actual_exe = ACTUAL_EXE.as_posix()
        check(lua.eval('dofile(script_dir.."native_guard.lua").verify(actual_exe)') == '26367565',
              'read-only guard verification passes against actual build 26367565 executable')

print(json.dumps({'passed': True, 'checks': len(CHECKS), 'results': CHECKS,
                  'physical_controller_tested': False, 'live_mapping_tested': False}, ensure_ascii=False, indent=2))
