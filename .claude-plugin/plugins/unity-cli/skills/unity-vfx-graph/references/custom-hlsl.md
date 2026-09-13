# Custom HLSL

Custom HLSL needs no dedicated op. The Custom HLSL **block** (descriptor `Custom HLSL`, category
`HLSL`) and the Custom HLSL **operator** (category `Operator/HLSL`) are library nodes: add them with
`add_block` / `add_operator`, then write the source with `set_block_setting` / `set_operator_setting`.

```bash
unity-cli raw vfx_apply --json '{"op":"add_block","assetPath":"Assets/FX/Burst.vfx","contextType":"Update","blockName":"Custom HLSL"}'
unity-cli raw vfx_apply --json '{"op":"set_block_setting","assetPath":"Assets/FX/Burst.vfx","contextType":"Update","blockIndex":0,"setting":"m_HLSLCode","value":"void Heat(inout VFXAttributes attributes, in float k){ attributes.position *= k; }"}'
unity-cli raw vfx_apply --json '{"op":"add_operator","assetPath":"Assets/FX/Burst.vfx","operatorName":"Custom HLSL","settings":{"m_HLSLCode":"float3 Swirl(in float3 p, in float t){ return p * t; }","m_OperatorName":"Swirl"}}'
```

## The 4-input limit (operators only)

A Custom HLSL **operator** compiles to a single VFX expression whose parents are its input slots, and
a VFX expression takes **at most 4 parents**. A function with 5 or more parameters makes the asset
importer throw `An expression can only take up to 4 parent expressions`; the graph silently stops
compiling and the effect disappears in the editor.

- `set_operator_setting` and `add_operator` **refuse** any source/file/function selection that would
  expose more than 4 inputs, with a clear error, and leave the previous state in place.
- Work around it by packing inputs into `float2`/`float3`/`float4` (or `Vector2`/`3`/`4` slots) or by
  splitting the function across two operators.
- The Custom HLSL **block** has no such limit: its parameters become properties of the generated
  shader, not expression parents. Attribute access goes through the `inout VFXAttributes attributes`
  parameter.
- If a graph somehow reaches that state (e.g. authored in the editor), the failure shows up as
  `compile.success:false` with that `exception` on the next op, in `compile`, and as a
  `CompileException` entry in describe `errors`.

## Settings

| Setting | Block | Operator | Meaning |
|---|---|---|---|
| `m_HLSLCode` | ✓ | ✓ | Inline source (ignored when `m_ShaderFile` is set) |
| `m_BlockName` / `m_OperatorName` | ✓ | ✓ | Displayed node name |
| `m_ShaderFile` | ✓ | ✓ | An `.hlsl` asset path (imported as `ShaderInclude`); the node sources from the file |
| `m_AvailableFunction` / `m_AvailableFunctions` | ✓ (singular) | ✓ (plural) | Function selector when the source defines several functions — pass the bare function name |

- Block functions: `void Name(inout VFXAttributes attributes, in <type> p, …)`; write attributes on
  `attributes.<name>`. Variadic attributes must be addressed per component (`positionX`, not `position`
  as a float). Missing `inout` when writing attributes is an Invalidate-tier error.
- Operator functions: `<returnType> Name(in <type> a, …)`, must return a value; `out`/`inout` parameters
  are not allowed.
- Parameter types map to slots: scalars/vectors, `VFXSampler2D`/`VFXSampler3D` → Texture2D/3D slots,
  `StructuredBuffer<T>`/`RWStructuredBuffer<T>` → GraphicsBuffer slots. `#include "Other.hlsl"` in an
  `m_ShaderFile` resolves relative to that file.
- The node re-parses its input slots from the selected function's signature after every
  source/file/selector change — confirm by re-describing `inputSlots`. Describe reports every setting
  including the read-only `m_HLSLCode`; the selector shows as `{selection, values}`.
- HLSL *parse* problems (unknown parameter type, missing function, bad attribute access) surface as
  Invalidate-tier `errors` in describe. Shader *compile* problems inside the function body surface in
  `compile.logs` after the op that triggered the import.
