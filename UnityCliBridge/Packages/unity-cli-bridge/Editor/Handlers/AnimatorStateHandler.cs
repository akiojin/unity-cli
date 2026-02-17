using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace UnityCliBridge.Handlers
{
    public static class AnimatorStateHandler
    {
        public static object GetAnimatorState(JObject parameters)
        {
            try
            {
                // Get parameters
                var gameObjectName = parameters["gameObjectName"]?.ToString();
                var includeParameters = parameters["includeParameters"]?.ToObject<bool>() ?? true;
                var includeStates = parameters["includeStates"]?.ToObject<bool>() ?? true;
                var includeTransitions = parameters["includeTransitions"]?.ToObject<bool>() ?? true;
                var includeClips = parameters["includeClips"]?.ToObject<bool>() ?? false;
                var layerIndex = parameters["layerIndex"]?.ToObject<int>() ?? -1; // -1 means all layers

                // Validate input
                if (string.IsNullOrEmpty(gameObjectName))
                {
                    return new { error = "gameObjectName is required" };
                }

                // Find the GameObject
                var targetObject = GameObject.Find(gameObjectName);
                if (targetObject == null)
                {
                    // Try to find in all objects including inactive
                    var allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
                    targetObject = allObjects.FirstOrDefault(go => go.name == gameObjectName && !EditorUtility.IsPersistent(go));
                }

                if (targetObject == null)
                {
                    return new { error = $"GameObject not found: {gameObjectName}" };
                }

                // Get Animator component
                var animator = targetObject.GetComponent<Animator>();
                if (animator == null)
                {
                    return new { error = $"Animator component not found on GameObject: {gameObjectName}" };
                }

                var result = new Dictionary<string, object>
                {
                    ["gameObject"] = gameObjectName,
                    ["isPlaying"] = Application.isPlaying,
                    ["enabled"] = animator.enabled,
                    ["hasController"] = animator.runtimeAnimatorController != null
                };

                bool hasController = animator.runtimeAnimatorController != null;
                if (hasController)
                {
                    // Get controller info
                    result["controllerName"] = animator.runtimeAnimatorController.name;
                }
                else
                {
                    result["controllerName"] = null;
                }

                // Get current state info (only available in play mode)
                if (hasController && Application.isPlaying && includeStates)
                {
                    var states = new List<Dictionary<string, object>>();
                    var layerCount = animator.layerCount;
                    
                    for (int i = 0; i < layerCount; i++)
                    {
                        if (layerIndex >= 0 && i != layerIndex) continue;

                        var stateInfo = animator.GetCurrentAnimatorStateInfo(i);
                        var nextStateInfo = animator.GetNextAnimatorStateInfo(i);
                        var layerName = animator.GetLayerName(i);
                        var layerWeight = animator.GetLayerWeight(i);

                        var layerData = new Dictionary<string, object>
                        {
                            ["layerIndex"] = i,
                            ["layerName"] = layerName,
                            ["layerWeight"] = layerWeight
                        };

                        // Current state
                        var currentState = new Dictionary<string, object>
                        {
                            ["fullPathHash"] = stateInfo.fullPathHash,
                            ["shortNameHash"] = stateInfo.shortNameHash,
                            ["normalizedTime"] = stateInfo.normalizedTime,
                            ["length"] = stateInfo.length,
                            ["speed"] = stateInfo.speed,
                            ["speedMultiplier"] = stateInfo.speedMultiplier,
                            ["tagHash"] = stateInfo.tagHash,
                            ["isLooping"] = stateInfo.loop
                        };

                        // Try to get state name (requires AnimatorController asset)
                        if (animator.runtimeAnimatorController is AnimatorController controller)
                        {
                            var stateMachine = controller.layers[i].stateMachine;
                            var state = FindStateByHash(stateMachine, stateInfo.fullPathHash);
                            if (state != null)
                            {
                                currentState["name"] = state.name;
                                if (includeClips && state.motion != null)
                                {
                                    currentState["motion"] = state.motion.name;
                                }
                            }
                        }

                        layerData["currentState"] = currentState;

                        // Transition info
                        if (includeTransitions && animator.IsInTransition(i))
                        {
                            var transitionInfo = animator.GetAnimatorTransitionInfo(i);
                            var transition = new Dictionary<string, object>
                            {
                                ["duration"] = transitionInfo.duration,
                                ["normalizedTime"] = transitionInfo.normalizedTime,
                                ["nameHash"] = transitionInfo.nameHash,
                                ["userNameHash"] = transitionInfo.userNameHash,
                                ["durationUnit"] = transitionInfo.durationUnit.ToString()
                            };

                            // Next state info
                            var nextState = new Dictionary<string, object>
                            {
                                ["fullPathHash"] = nextStateInfo.fullPathHash,
                                ["shortNameHash"] = nextStateInfo.shortNameHash,
                                ["normalizedTime"] = nextStateInfo.normalizedTime,
                                ["length"] = nextStateInfo.length
                            };

                            if (animator.runtimeAnimatorController is AnimatorController controller2)
                            {
                                var stateMachine = controller2.layers[i].stateMachine;
                                var state = FindStateByHash(stateMachine, nextStateInfo.fullPathHash);
                                if (state != null)
                                {
                                    nextState["name"] = state.name;
                                }
                            }

                            transition["nextState"] = nextState;
                            layerData["activeTransition"] = transition;
                        }

                        states.Add(layerData);
                    }

                    result["layers"] = states;
                }
                else if (!hasController)
                {
                    result["layers"] = new List<Dictionary<string, object>>();
                }

                // Get parameters
                if (includeParameters)
                {
                    var animatorParams = new Dictionary<string, object>();
                    foreach (var param in animator.parameters)
                    {
                        var paramData = new Dictionary<string, object>
                        {
                            ["type"] = param.type.ToString()
                        };

                        switch (param.type)
                        {
                            case AnimatorControllerParameterType.Float:
                                paramData["value"] = animator.GetFloat(param.name);
                                paramData["defaultValue"] = param.defaultFloat;
                                break;
                            case AnimatorControllerParameterType.Int:
                                paramData["value"] = animator.GetInteger(param.name);
                                paramData["defaultValue"] = param.defaultInt;
                                break;
                            case AnimatorControllerParameterType.Bool:
                                paramData["value"] = animator.GetBool(param.name);
                                paramData["defaultValue"] = param.defaultBool;
                                break;
                            case AnimatorControllerParameterType.Trigger:
                                // Triggers don't have a readable state in runtime
                                paramData["value"] = "N/A (Trigger)";
                                break;
                        }

                        animatorParams[param.name] = paramData;
                    }
                    result["parameters"] = animatorParams;
                }

                // Build summary
                var summary = $"Animator state retrieved for '{gameObjectName}'";
                if (Application.isPlaying)
                {
                    if (includeStates)
                    {
                        summary += $" - {animator.layerCount} layer(s)";
                    }
                    if (includeParameters)
                    {
                        summary += $", {animator.parameters.Length} parameter(s)";
                    }
                }
                else
                {
                    summary += " (Editor mode - limited state info available)";
                }
                result["summary"] = summary;

                return result;
            }
            catch (Exception e)
            {
                return new { error = $"Failed to get animator state: {e.Message}" };
            }
        }

        public static object GetAnimatorRuntimeInfo(JObject parameters)
        {
            try
            {
                if (!Application.isPlaying)
                {
                    return new { error = "This command is only available in Play mode" };
                }

                // Get parameters
                var gameObjectName = parameters["gameObjectName"]?.ToString();
                var includeIK = parameters["includeIK"]?.ToObject<bool>() ?? true;
                var includeRootMotion = parameters["includeRootMotion"]?.ToObject<bool>() ?? true;
                var includeBehaviours = parameters["includeBehaviours"]?.ToObject<bool>() ?? false;

                // Validate input
                if (string.IsNullOrEmpty(gameObjectName))
                {
                    return new { error = "gameObjectName is required" };
                }

                // Find the GameObject
                var targetObject = GameObject.Find(gameObjectName);
                if (targetObject == null)
                {
                    return new { error = $"GameObject not found: {gameObjectName}" };
                }

                // Get Animator component
                var animator = targetObject.GetComponent<Animator>();
                if (animator == null)
                {
                    return new { error = $"Animator component not found on GameObject: {gameObjectName}" };
                }

                var result = new Dictionary<string, object>
                {
                    ["gameObject"] = gameObjectName,
                    ["isPlaying"] = true,
                    ["enabled"] = animator.enabled,
                    ["updateMode"] = animator.updateMode.ToString(),
                    ["cullingMode"] = animator.cullingMode.ToString()
                };

                // Runtime properties
                result["playbackTime"] = animator.playbackTime;
                result["recorderStartTime"] = animator.recorderStartTime;
                result["recorderStopTime"] = animator.recorderStopTime;
                result["runtimeAnimatorController"] = animator.runtimeAnimatorController?.name ?? "None";
                
                // Avatar info
                if (animator.avatar != null)
                {
                    var avatarInfo = new Dictionary<string, object>
                    {
                        ["name"] = animator.avatar.name,
                        ["isValid"] = animator.avatar.isValid,
                        ["isHuman"] = animator.avatar.isHuman
                    };
                    result["avatar"] = avatarInfo;
                }

                // Speed and time scale
                result["speed"] = animator.speed;
                result["deltaPosition"] = new
                {
                    x = animator.deltaPosition.x,
                    y = animator.deltaPosition.y,
                    z = animator.deltaPosition.z
                };
                result["deltaRotation"] = new
                {
                    x = animator.deltaRotation.x,
                    y = animator.deltaRotation.y,
                    z = animator.deltaRotation.z,
                    w = animator.deltaRotation.w
                };

                // Root motion
                if (includeRootMotion)
                {
                    var rootMotion = new Dictionary<string, object>
                    {
                        ["applyRootMotion"] = animator.applyRootMotion,
                        ["hasRootMotion"] = animator.hasRootMotion,
                        ["velocity"] = new
                        {
                            x = animator.velocity.x,
                            y = animator.velocity.y,
                            z = animator.velocity.z
                        },
                        ["angularVelocity"] = new
                        {
                            x = animator.angularVelocity.x,
                            y = animator.angularVelocity.y,
                            z = animator.angularVelocity.z
                        },
                        ["rootPosition"] = new
                        {
                            x = animator.rootPosition.x,
                            y = animator.rootPosition.y,
                            z = animator.rootPosition.z
                        },
                        ["rootRotation"] = new
                        {
                            x = animator.rootRotation.x,
                            y = animator.rootRotation.y,
                            z = animator.rootRotation.z,
                            w = animator.rootRotation.w
                        }
                    };
                    result["rootMotion"] = rootMotion;
                }

                // IK info (only for humanoid)
                if (includeIK && animator.isHuman)
                {
                    var ikInfo = new Dictionary<string, object>
                    {
                        ["isHuman"] = true,
                        ["humanScale"] = animator.humanScale,
                        ["isMatchingTarget"] = animator.isMatchingTarget,
                        ["feetPivotActive"] = animator.feetPivotActive,
                        ["pivotWeight"] = animator.pivotWeight,
                        ["pivotPosition"] = new
                        {
                            x = animator.pivotPosition.x,
                            y = animator.pivotPosition.y,
                            z = animator.pivotPosition.z
                        }
                    };

                    // IK position and rotation weights for each limb
                    var ikGoals = new Dictionary<string, object>();
                    foreach (AvatarIKGoal goal in Enum.GetValues(typeof(AvatarIKGoal)))
                    {
                        ikGoals[goal.ToString()] = new Dictionary<string, object>
                        {
                            ["positionWeight"] = animator.GetIKPositionWeight(goal),
                            ["rotationWeight"] = animator.GetIKRotationWeight(goal),
                            ["position"] = new
                            {
                                x = animator.GetIKPosition(goal).x,
                                y = animator.GetIKPosition(goal).y,
                                z = animator.GetIKPosition(goal).z
                            },
                            ["rotation"] = new
                            {
                                x = animator.GetIKRotation(goal).x,
                                y = animator.GetIKRotation(goal).y,
                                z = animator.GetIKRotation(goal).z,
                                w = animator.GetIKRotation(goal).w
                            }
                        };
                    }
                    ikInfo["goals"] = ikGoals;

                    result["ikInfo"] = ikInfo;
                }

                // State Machine Behaviours
                // Note: GetCurrentAnimatorStateBehaviours is only available at runtime with specific setup
                // Removed for compatibility with Unity 2020.3+

                // Performance info
                result["hasBoundPlayables"] = animator.hasBoundPlayables;
                result["hasTransformHierarchy"] = animator.hasTransformHierarchy;
                result["isOptimizable"] = animator.isOptimizable;
                result["gravityWeight"] = animator.gravityWeight;
                result["bodyPosition"] = animator.isHuman ? new
                {
                    x = animator.bodyPosition.x,
                    y = animator.bodyPosition.y,
                    z = animator.bodyPosition.z
                } : null;
                result["bodyRotation"] = animator.isHuman ? new
                {
                    x = animator.bodyRotation.x,
                    y = animator.bodyRotation.y,
                    z = animator.bodyRotation.z,
                    w = animator.bodyRotation.w
                } : null;

                result["summary"] = $"Animator runtime info retrieved for '{gameObjectName}'";

                return result;
            }
            catch (Exception e)
            {
                return new { error = $"Failed to get animator runtime info: {e.Message}" };
            }
        }

        private static AnimatorState FindStateByHash(AnimatorStateMachine stateMachine, int hash)
        {
            // Check direct states
            foreach (var state in stateMachine.states)
            {
                if (Animator.StringToHash(state.state.name) == hash ||
                    Animator.StringToHash(stateMachine.name + "." + state.state.name) == hash)
                {
                    return state.state;
                }
            }

            // Check sub state machines
            foreach (var subStateMachine in stateMachine.stateMachines)
            {
                var foundState = FindStateByHash(subStateMachine.stateMachine, hash);
                if (foundState != null)
                {
                    return foundState;
                }
            }

            return null;
        }
    }
}
