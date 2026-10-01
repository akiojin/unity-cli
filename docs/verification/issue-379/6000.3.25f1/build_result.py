#!/usr/bin/env python3
"""Assemble result.json from the measured raw outputs in this directory (no hand-typed pass flags)."""
import json, hashlib, pathlib
R = pathlib.Path(__file__).resolve().parent
P = R.parent / "Project"
def j(name): return json.loads((R / "raw" / name).read_text())
def val(name): d = j(name); return d.get("value", d)
def sha(p): return hashlib.sha256(pathlib.Path(p).read_bytes()).hexdigest()
def hashes(f): return {l.split()[1]: l.split()[0] for l in (R / "hashes" / f).read_text().splitlines()}

def assert_rec(label):
    d = j(f"{label}.out"); v = d["value"]
    return {"state": d["state"], "total": v["total"], "failed": v["failed"], "passed": d["state"] == "completed" and v["failed"] == 0,
            "failures": [c for c in v["checks"] if not c["pass"]], "isPlaying": v["isPlaying"], "frameCount": v["frameCount"],
            "rendererVisible": v["rendererVisible"], "shaderSupported": v["shaderSupported"], "spritePacked": v["spritePacked"],
            "spritePackerMode": v["spritePackerMode"], "info": v["info"], "evidence": f"raw/{label}.out", "script": f"eval/{label}.cs"}

skill_before, skill_after = hashes("skill-before.sha256"), hashes("skill-after.sha256")
imp = j("012-import-modify-skill-example.out"); ppu = val("013-recipe-ppu-single.out")
atl = j("015-atlas-create-skill-example.out"); atr = val("016-atlas-readback.out"); pre = val("014-atlas-precheck.out")
rt_pre = val("017-ruletile-toggle-presave.out")
initial, reload, play, stop = (assert_rec(x) for x in ("010-assert-initial", "022-assert-after-reload", "028-assert-in-play", "035-assert-after-stop"))
cap = j("027-capture-game-skill-example.out"); ana = j("029-screenshot-analysis.json")
shot = R / "screenshots/game-play-1280x720.png"
c_before, c_play, c_stop = (j(x)["statistics"] for x in ("023-console-before-play.out", "031-console-in-play-full.out", "038-console-after-stop.out"))
comp_play, comp_stop = j("032-compilation-in-play.out"), j("039-compilation-after-stop.out")
ver = val("040-versions.out")
play_poll = j("025-editor-state-play-poll-1.out")["state"]; stop_poll = j("034-editor-state-after-stop-1.out")["state"]
save = j("019-save-scene-skill-example.out"); load = j("021-load-scene.out")
proj_diff = (R / "raw/043-project-hash-diff.txt").read_text().splitlines()
closed = (R / "raw/042-editor-closed.txt").read_text()

assertions = {
    "skillFilesUnchangedDuringRun": skill_before == skill_after,
    "importModifySuccess": imp.get("success") is True and imp["newSettings"]["textureType"] == "Sprite" and imp["newSettings"]["filterMode"] == "Point" and imp["newSettings"]["generateMipMaps"] is False,
    "recipePpuSingle16x16Point": ppu == {"filter": "Point", "height": 16.0, "mode": "Single", "spritePixelsPerUnit": 16.0, "width": 16.0},
    "newAtlasDidNotPreexist": pre["mainAssetExists"] is False and pre["fileExists"] is False,
    "atlasCreateSuccess": atl.get("success") is True and atl.get("packableCount") == 1,
    "atlasReadbackPointNoMipNoRotationPackables": atr["packables"] == ["Assets/Tiles/grass.png"] and atr["texture"]["filter"] == "Point" and atr["texture"]["generateMipMaps"] is False and atr["packing"]["enableRotation"] is False,
    "ruleTileDynamicMatchNonmatchPreSave": rt_pre["pass"] is True,
    "stateInitial": initial["passed"], "stateAfterSaveReload": reload["passed"], "stateInPlay": play["passed"], "stateAfterStop": stop["passed"],
    "saveSceneSavedNotDirty": save["saved"] is True and save["isDirty"] is False,
    "reloadedFromDisk": load["isLoaded"] is True and val("020-open-empty-scene.out")["gridStillLoaded"] is False,
    "playStateConfirmed": play_poll["isPlaying"] is True and play["isPlaying"] is True,
    "captureRequestGameNoFallback": cap["captureMode"] == "game" and cap["width"] == 1280 and cap["height"] == 720,
    "capturePixelExact": ana["allTilesPixelExact"] and ana["onlyExpectedColours"] and ana["nonBackgroundPixelsOutsideTiles"] == 0 and ana["backgroundPixelsInsideTiles(seams)"] == 0 and ana["nonBackgroundBBox"] == ana["expectedBBox"],
    "pixelPerfectRatio4InPlay": play["info"]["pixelRatio"] == 4,
    "zeroConsoleErrorsInPlayAndAfterStop": all(c["errors"] == 0 and c["exceptions"] == 0 and c["asserts"] == 0 for c in (c_play, c_stop)),
    "zeroCompilationErrors": comp_play["errorCount"] == 0 and comp_stop["errorCount"] == 0,
    "stoppedToEditMode": stop_poll["isPlaying"] is False and stop["isPlaying"] is False,
    "atlasOnlySpritesNotPacked": all(not v for r in (initial, reload, play, stop) for v in r["spritePacked"].values()),
    "onlyNewAtlasAddedToProject": all(l.startswith(">") and "TerrainRecheck.spriteatlas" in l for l in proj_diff if l.startswith(("<", ">"))),
    "editorClosedPortFree": "no such pid" in closed and "port 6579 free" in closed,
}
failed = [k for k, v in assertions.items() if not v]

result = {
    "skill": "unity-2d-sprite-tilemap",
    "run": {
        "kind": "focused final-skill recheck (fresh measured run after final skill changes)",
        "date": "2026-10-01",
        "originalRun": "evidence/ (preserved; not written by this recheck. evidence/result.json mtime 17:39 JST was written by the original run's own process before this recheck started writing)",
        "evidenceDir": str(R), "projectPath": "/tmp/issue379-live/6000.3.25f1/Project (bridge reports /private/tmp/...)",
        "port": 6579, "portOnEveryCall": "every unity-cli call went through evidence-recheck/u.sh (--port 6579 --project-path <Project>, UNITY_CLI_PORT=6579); see commands.log",
        "editorPid": 81589,
        "editorLaunch": "PID 31815 was not running and doctor reported EDITOR_NOT_RUNNING with no Unity process for this project (raw/001, raw/002; the 2 grep hits are harness shells whose argv text contains the path). Launched directly with the exact command from the request: UNITY_CLI_PORT=6579 /Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity -projectPath /tmp/issue379-live/6000.3.25f1/Project -logFile evidence-recheck/editor.log. `unity-cli setup --launch-editor` was not tried first; direct launch was chosen to get an isolated -logFile (needed to read the capture source). No lock files were deleted (Project/Temp did not exist).",
        "skillInvokedViaSkillTool": "unity-2d-sprite-tilemap (see recheck-stream.jsonl)",
    },
    "skillUnderTest": {
        "canonicalDir": "/Users/akiojin/Workbench/unity-cli/work/issue-379/.claude-plugin/plugins/unity-cli/skills/unity-2d-sprite-tilemap",
        "installedCopyLoadedBySkillTool": "/private/tmp/issue379-live/6000.3.25f1/.claude/skills/unity-2d-sprite-tilemap (symlink to canonicalDir)",
        "installedCopyIdenticalToCanonical": hashes("skill-installed-symlink-before.sha256") == skill_before,
        "sha256": skill_before, "sha256After": skill_after, "unchangedDuringRun": skill_before == skill_after,
        "evidence": ["hashes/skill-before.sha256", "hashes/skill-after.sha256", "hashes/skill-installed-symlink-before.sha256"],
    },
    "overall": {
        "status": "passed" if not failed else "failed",
        "assertions": assertions, "failedAssertions": failed,
        "summary": "Corrected SKILL.md examples executed with only the atlas path adapted (import path already matched the example). import action:modify succeeded; recipe PPU/Single eval completed; create_sprite_atlas with packingSettings.allowRotation:false created a NEW atlas whose readback shows Point/no mips/no rotation. Save -> empty scene -> reload, Play, Stop state assertions 70/70 each; capture pixel-exact. Atlas verified as an asset only: Sprite Packer mode Disabled and Sprite.packed false.",
        "versions": {"unityEditor": ver["unity"], "renderPipeline": ver["pipeline"], "graphicsDevice": ver["graphicsDevice"], "unityCli": "0.16.0",
                     "bridge": "com.akiojin.unity-cli-bridge 0.16.0 (ping versionCheck match, raw/004)", "packages": ver["packages"], "packagesChanged": False},
    },
    "correctedExamples": {
        "importModify": {"sent": '{"action":"modify","assetPath":"Assets/Tiles/grass.png","settings":{"textureType":"Sprite","filterMode":"Point","generateMipMaps":false,"compressionQuality":100}}',
                         "pathAdapted": "none (fixture path equals the example)", "result": imp,
                         "note": "Values were already set (raw/011 get); grass.png.meta stayed byte-identical. Proves the corrected action is accepted, not that it changes values.",
                         "evidence": ["raw/011-import-get-before.out", "raw/012-import-modify-skill-example.out"]},
        "ppuRecipeEval": {"state": j("013-recipe-ppu-single.out")["state"], "measured": ppu, "evidence": "raw/013-recipe-ppu-single.out"},
        "atlasCreate": {"sent": '{"atlasPath":"Assets/Tiles/TerrainRecheck.spriteatlas","packables":["Assets/Tiles/grass.png"],"packingSettings":{"allowRotation":false},"textureSettings":{"filterMode":"Point","generateMipMaps":false}}',
                        "pathAdapted": "atlasPath Terrain.spriteatlas -> TerrainRecheck.spriteatlas (NEW; precheck raw/014 showed it absent); existing Assets/Tiles/Terrain.spriteatlas untouched",
                        "overwriteUsed": False, "result": atl, "readback": atr,
                        "readbackNotes": ["`isVariant` key in raw/016 is mislabeled by my snippet: it holds IsIncludeInBuild()==true; the YAML shows m_IsVariant: 0.",
                                          "Defaults kept by the example: enableTightPacking true, padding 4; recipe only says 'appropriate padding', so not treated as a failure."],
                        "scope": "Asset-only atlas test. EditorSettings.spritePackerMode=Disabled, atlas.spriteCount=0, CanBindTo(grass)=false, Sprite.packed=false in every phase incl. Play. SpriteAtlasUtility.PackAtlases was deliberately NOT called (it crashed Unity natively in the original run).",
                        "evidence": ["raw/014-atlas-precheck.out", "raw/015-atlas-create-skill-example.out", "raw/016-atlas-readback.out", "snapshots/01-after-close/Assets/Tiles/TerrainRecheck.spriteatlas"]},
        "saveScene": {"result": save, "evidence": "raw/019-save-scene-skill-example.out"},
        "playGame": "raw/024-play-game-skill-example.out", "getEditorState": "raw/026-get-editor-state-skill-example.out",
        "captureScreenshot": "raw/027-capture-game-skill-example.out", "readConsole": "raw/030-console-in-play-skill-example.out", "stopGame": "raw/033-stop-game-skill-example.out",
    },
    "persistence": {
        "assertionScript": "eval/assert-state.template.cs, generated by make_assert.py from the original evidence/eval/56b-assert-after-reload.cs plus: isPlaying expectation, new-atlas checks, Sprite.packed=false check, renderer/shader info",
        "checks": "importer PPU16/Single/Point/no mips/Uncompressed/16x16 for grass+edge; original atlas packables/settings; new atlas packables/settings; Grass.asset + GrassRule.asset (rule (1,0,0) This -> edge); single Grid/Tilemap, Sprites/Default; cells (0,0),(1,0),(2,0)=Grass/grass, (0,2)=GrassRule/edge MATCH, (1,2)=GrassRule/grass NONMATCH, (1,1),(2,2) empty, 5 used cells; one ortho Main Camera at (1.5,1.5,-10); one UnityEngine.U2D.PixelPerfectCamera assetsPPU16 ref 320x180",
        "initial": initial,
        "ruleTileDynamicPreSave": {"result": rt_pre, "evidence": "raw/017-ruletile-toggle-presave.out", "caveat": "raw/017 was overwritten by an unintended identical-label rerun after Stop (raw/017-NOTE.txt); both runs returned the same values; copy at raw/036-*. Scene was not dirty afterwards (raw/037)."},
        "save": {"saveAssets": "raw/018-mark-dirty-save-assets.out", "saveScene": "raw/019-save-scene-skill-example.out", "sceneFileByteIdenticalToPreRun": True},
        "reload": {"emptyScene": "raw/020-open-empty-scene.out (Grid no longer loaded)", "loadScene": "raw/021-load-scene.out (Single)"},
        "afterReload": reload, "afterStop": stop,
    },
    "play": {
        "playGame": "raw/024 (scheduled)", "isPlayingConfirmed": play_poll["isPlaying"], "isPlayingEvidence": "raw/025-editor-state-play-poll-1.out, raw/026",
        "inPlayAssertions": play,
        "console": {"beforePlayEditMode": c_before, "beforePlayNote": "The 1 Error is Unity 6's startup 'This project uses Input Manager, which is marked for deprecation' message, present at Editor launch (raw/008) before any recheck mutation; it is not in the Play or after-Stop console.",
                    "inPlay": c_play, "afterStop": c_stop, "warningsAreBridgeLogsOnly": True},
        "compilation": {"inPlay": comp_play, "afterStop": comp_stop},
        "stop": "raw/033-stop-game-skill-example.out (message 'Exited play mode'; embedded state still showed isPlaying true at response time); raw/034-editor-state-after-stop-1.out isPlaying false, reason ready",
        "inputSimulation": "not applicable (static art scene)",
    },
    "capture": {
        "request": {"captureMode": "game", "width": 1280, "height": 720, "osFallback": False},
        "toolResult": cap,
        "actualSource": "Main Camera rendered by the bridge in Play Mode (tool message 'Screenshot captured successfully from Main Camera'; editor.log: '[ScreenshotHandler] CaptureGameView called' then 'Using main camera in Play Mode'). This is a camera render, NOT the final Game-view backbuffer; it does not prove UI/post-processing/final composited output (unity-cli #422). The scene has no UI or post-processing.",
        "path": "evidence-recheck/screenshots/game-play-1280x720.png", "originalToolPath": cap["path"],
        "sha256": sha(shot),
        "identicalToOriginalRunCapture": sha(shot) == sha(R.parent / "evidence/screenshots/game_play_1280x720.png"),
        "inspectedWithReadTool": True,
        "observedByEye": "Dark navy background; upper row: orange/brown vertically striped edge tile (RuleTile MATCH) with a green grass tile to its right (NONMATCH); lower row: three green grass tiles with dark border/dots; hard 4x pixel blocks, no blur, no seams, no magenta.",
        "pixelAssertions": {k: ana[k] for k in ("width", "height", "uniqueColourCount", "onlyExpectedColours", "nonBackgroundBBox", "expectedBBox", "pixelScale", "nonBackgroundPixelsOutsideTiles", "backgroundPixelsInsideTiles(seams)", "allTilesPixelExact", "tiles", "uniqueColours")},
        "analysisScript": "analyze_screenshot.py (copied unchanged from evidence/)", "evidence": ["raw/027-capture-game-skill-example.out", "raw/029-screenshot-analysis.json", "editor.log"],
    },
    "failures": [
        {"id": "017-overwrite", "kind": "evidence-handling slip (mine)", "detail": "Reran eval 017 under the same label after Stop, overwriting the pre-save output file. Values identical; documented in raw/017-NOTE.txt. Not a skill defect."},
    ],
    "failedCommands": [{"id": "000-ping", "detail": "Connection refused before Editor launch (expected; Editor not running)"}],
    "projectChanges": {
        "addedFiles": ["Assets/Tiles/TerrainRecheck.spriteatlas", "Assets/Tiles/TerrainRecheck.spriteatlas.meta"],
        "modifiedFiles": [], "hashDiff": proj_diff,
        "userSettingsLayoutChanged": "UserSettings/Layouts/default-6000.dwlt (Editor window layout written on exit)",
        "sceneByteIdenticalToPreRun": True, "packagesManifestChanged": False,
        "snapshots": ["snapshots/00-before (Assets/Packages/ProjectSettings/UserSettings)", "snapshots/01-after-close", "snapshots/capture-dir"],
        "evidence": ["hashes/project-before.sha256", "hashes/project-after.sha256", "raw/043-project-hash-diff.txt", "raw/044-snapshot-diff.txt"],
    },
    "cleanup": {"editorClosedVia": "EditorApplication.Exit(0) via delayCall after scene verified not dirty and SaveAssets (raw/041)", "editorPidExited": "no such pid" in closed,
                "port6579FreeAfterExit": "port 6579 free" in closed, "projectTempRemoved": "Temp: No such file" in closed,
                "otherEditorsUntouched": "raw/042-editor-closed.txt lists other projects' Editors still running", "gitUsed": False, "repositoryFilesEdited": False, "serializedYamlHandEdited": False,
                "previousEvidencePreserved": True, "packAtlasesCalled": False},
    "observationsForSkill": [
        "Corrected import (action:modify) and atlas (packingSettings.allowRotation:false) examples work as written on 6000.3.25f1 / bridge 0.16.0.",
        "create_sprite_atlas leaves enableTightPacking=true and padding=4 by default; the recipe asks only for 'appropriate padding', which these satisfy, but a pixel-art example could state them explicitly.",
        "SKILL.md step 7 still says 'capture the Game view'; on bridge 0.16.0 that is a Main Camera render. The recipe's #422 caveat covers this.",
    ],
}
(R / "result.json").write_text(json.dumps(result, indent=1, ensure_ascii=False))
print(result["overall"]["status"], failed)
