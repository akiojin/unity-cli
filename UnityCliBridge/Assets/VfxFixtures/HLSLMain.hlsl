#include "HLSLInclude.hlsl"

void SquashTwice(inout VFXAttributes attributes, in float k)
{
    Squash(attributes, k);
    Squash(attributes, k);
}
