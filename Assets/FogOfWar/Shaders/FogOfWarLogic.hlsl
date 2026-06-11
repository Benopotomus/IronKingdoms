#ifndef _CONESETUP_
#define _CONESETUP_

struct RevealerInfoStruct
{
    int startIndex;
    
    float revealerRadius;   //excludes distance added from revealerFadeDistance
    float revealerFadeDistance;

    float innerSoftenThreshold;
    float invInnerSoftenThreshold;

    float unobscuredRadius; //excludes distance added from unobscuredSoftenRadius
    float unobscuredSoftenRadius;

    float visionHeight;
    float heightFade;

    float revealerOpacity;

    bool useOcclusion;
};

struct RevealerDataStruct
{
    float totalRevealerRadius;
    float2 revealerPosition;
    float revealerHeight;
    int numSegments;
};

struct RevealerSightSegment
{
    float2 segmentDirection;
    float length;
};

#pragma multi_compile FOW_HARD FOW_SOFT
#pragma multi_compile _ FOW_USE_SPATIAL_HASHING
#pragma multi_compile _ FOW_PIXELATE
//#pragma multi_compile_local _ INNER_SOFTEN
#pragma multi_compile _ FOW_SAMPLE_REALTIME
#pragma multi_compile _ FOW_USE_TEXTURE_BLUR
#pragma multi_compile _ FOW_BLEED_ON
#pragma multi_compile _ FOW_DITHER_ON

float FowEffectStrength;

bool BLEND_MAX;
int _fadeType;
bool _invertEffect;

float _ditherSize;

//bool _pixelate;
bool _pixelateWS;
float _pixelDensity;
float2 _pixelOffset;

float _extraRadius;

//float _fadeOutDegrees;
//float _innerSoftenThreshold;
//float _invInnerSoftenThreshold;
float _edgeSoftenDistance;

StructuredBuffer<RevealerInfoStruct> _RevealerInfoBuffer;
StructuredBuffer<RevealerDataStruct> _RevealerDataBuffer;
StructuredBuffer<RevealerSightSegment> _SightSegmentBuffer;

//no spatial hash path
int _NumRevealers;
StructuredBuffer<int> _ActiveRevealerIndices;

//spatial hash path
StructuredBuffer<int2> _GridRanges;
StructuredBuffer<int> _RevealerGridIds;
int _TableSize;
float _CellSize;

sampler2D _FowRT;
float4 _FowRT_TexelSize;
int _Sample_Blur_Quality;
float _Sample_Blur_Amount;
float4 _worldBounds;
float _worldBoundsSoftenDistance;
float _worldBoundsInfluence;

float _fadePower;

float lineThickness = .1;

int _fowPlane;

//2D variables
float _cameraSize;
float2 _cameraPosition;
float _cameraRotation;

//float Unity_InverseLerp_float4(float4 A, float4 B, float4 T)
//{
//    return (T - A) / (B - A);
//}

bool IsOne(half value)
{
    return value > .999;
    //return (abs(1 - value) < .001);
}

// Gaussian-weighted texture blur. Smoother falloff than box blur, same sample count.
float SampleBlurredTexture(float2 UV, int quality)
{
    float sum = 0;
    float totalWeight = 0;
    int upper = quality;
    int lower = -upper;
    float sigma = max((float)quality * _Sample_Blur_Amount, 0.5);
    float invTwoSigmaSq = 0.5 / (sigma * sigma);

    [loop]
    for (int x = lower; x <= upper; ++x)
    {
        [loop]
        for (int y = lower; y <= upper; ++y)
        {
            float weight = exp(-(float)(x * x + y * y) * invTwoSigmaSq);
            totalWeight += weight;

            float2 offset = float2(_FowRT_TexelSize.x * x, _FowRT_TexelSize.y * y) * _Sample_Blur_Amount;
            sum += (1 - tex2D(_FowRT, UV + offset).r) * weight;
        }
    }

    return sum / totalWeight;
}

void TextureSample_float(float2 Position, inout float coneOut)
{
#if FOW_SAMPLE_REALTIME || defined(IS_FOW_RENDERTEXTURE)
#else
    float2 uv = float2((((Position.x - _worldBounds.y) + (_worldBounds.x / 2)) / _worldBounds.x),
                 (((Position.y - _worldBounds.w) + (_worldBounds.z / 2)) / _worldBounds.z));
    
    float halfX = _worldBounds.x * 0.5;
    float halfZ = _worldBounds.z * 0.5;
    
    float minX = _worldBounds.y - halfX;
    float maxX = _worldBounds.y + halfX;
    float minY = _worldBounds.w - halfZ;
    float maxY = _worldBounds.w + halfZ;
    
    float outsideX = max(0, minX - Position.x) + max(0, Position.x - maxX);
    float outsideY = max(0, minY - Position.y) + max(0, Position.y - maxY);
    float outsideDist = length(float2(outsideX, outsideY));
    
    float boundsFade = saturate(1 - outsideDist / 1);

#if FOW_USE_TEXTURE_BLUR
    float texSamp = SampleBlurredTexture(uv, _Sample_Blur_Quality);
#else
    float texSamp = 1-tex2D(_FowRT, uv).r;
#endif
    texSamp*=boundsFade;
    coneOut = texSamp;
    //return;
    //if (Position.x > _worldBounds.y + (_worldBounds.x / 2) ||
    //    Position.x < _worldBounds.y - (_worldBounds.x / 2) ||
    //    Position.y > _worldBounds.w + (_worldBounds.z / 2) ||
    //    Position.y < _worldBounds.w - (_worldBounds.z / 2))
    //{
    //    texSamp = 0;
    //}
    
    ////texSamp = Unity_InverseLerp_float4(0, .52, texSamp);
    ////coneOut = float2(uv.x, 0);
    ////coneOut = lerp(texSamp, coneOut, coneOut);
    //coneOut = max(texSamp, coneOut);
    ////coneOut += texSamp;
    //coneOut = clamp(coneOut, 0, 1);
#endif
}

//shamelessly stolen from a generated shadergraph
void Dither(float In, float2 uv, out float Out)
{
    uv *= _ditherSize;
    //float2 uv = ScreenPosition.xy * _ScreenParams.xy;
    static const float DITHER_THRESHOLDS[16] =
    {
        1.0 / 17.0, 9.0 / 17.0, 3.0 / 17.0, 11.0 / 17.0,
        13.0 / 17.0, 5.0 / 17.0, 15.0 / 17.0, 7.0 / 17.0,
        4.0 / 17.0, 12.0 / 17.0, 2.0 / 17.0, 10.0 / 17.0,
        16.0 / 17.0, 8.0 / 17.0, 14.0 / 17.0, 6.0 / 17.0
    };
    uint index = (uint(uv.x) % 4) * 4 + uint(uv.y) % 4;
    Out = ceil(saturate(In - DITHER_THRESHOLDS[index]));
}

void CustomCurve_float(float In, out float Out)
{
    Out = In; //fade type 1; linear
    if (_invertEffect)
        Out = 1 - Out;
    switch (_fadeType)
    {
        case 0: //Linear Fade
            return;
        case 1: //Smooth Fade
            Out = sin(Out * 1.570796);
            return;
        case 2: //Smoother Fade
            Out = .5 - (cos(Out * 3.1415926) * .5);
            return;
        case 3: //Smoothstep Fade
            Out = smoothstep(0, 1, In);
            return;
        case 4: //Exponential Fade
            Out = pow(Out, _fadePower);
            return;
    }
}

void CustomCurve_half(half In, out half Out)
{
    Out = In; //fade type 1; linear
    if (_invertEffect)
        Out = 1 - Out;
    switch (_fadeType)
    {
        case 0: //Linear Fade
            return;
        case 1: //Smooth Fade
            Out = sin(Out * 1.570796);
            return;
        case 2: //Smoother Fade
            Out = .5 - (cos(Out * 3.1415926) * .5);
            return;
        case 3: //Smoothstep Fade
            Out = smoothstep(0, 1, In);
            return;
        case 4: //Exponential Fade
            Out = pow(Out, _fadePower);
            return;
    }
}

void OutOfBoundsCheck(float2 Position, inout float4 color)
{
//#if FOW_USE_WORLD_BOUNDS
    float OOBX = max(0, ((Position.x + _worldBoundsSoftenDistance) - (_worldBounds.y + (_worldBounds.x / 2))));
    OOBX = max(OOBX, -(Position.x - _worldBoundsSoftenDistance - (_worldBounds.y - (_worldBounds.x / 2))));
    float OOBY = max(0, ((Position.y + _worldBoundsSoftenDistance) - (_worldBounds.w + (_worldBounds.z / 2))));
    OOBY = max(OOBY, -((Position.y - _worldBoundsSoftenDistance) - (_worldBounds.w - (_worldBounds.z / 2))));
    
    float OOB = length((float2(OOBX, OOBY)));
    OOB = saturate(OOB / _worldBoundsSoftenDistance);
    OOB *= _worldBoundsInfluence;
    //CustomCurve_float(OOB, OOB);
    color = lerp(color, float4(0, 0, 0, 1), OOB * _worldBoundsInfluence);
    //if (Position.x > _worldBounds.y + (_worldBounds.x/2) ||
    //    Position.x < _worldBounds.y - (_worldBounds.x/2) ||
    //    Position.y > _worldBounds.w + (_worldBounds.z/2) ||
    //    Position.y < _worldBounds.w - (_worldBounds.z/2))
    //{
    //    color = lerp(color, float4(0, 0, 0, 1), _worldBoundsInfluence);
    //}
//#endif
}

half CalculateFadeZonePercent(half segmentHardDistance, half SoftenDistance, half DistanceToOrigin)
{
    return saturate(((segmentHardDistance + SoftenDistance) - DistanceToOrigin) / SoftenDistance);
}

half SmoothValue(half val)
{
    //return val;
    return smoothstep(0, 1, val);
    //val = clamp(val, 0, 1);
    //return sin(val * 1.570796);;
    //return 1 - (cos(val * 3.14159) * .5 + .5);
}

float SignedDelta(float a, float b)
{
    float d = a - b;
    return d - 360.0 * floor((d + 180.0) / 360.0);
    //float d = a - b;
    //d = fmod(d + 180.0, 360.0);
    //if (d < 0) d += 360.0;
    //return d - 180.0;
}

float2 CalculateIntersectionCramersRule(float2 start, float2 end, float2 relativePosition)
{
    //find the line intersection (cramers rule)
    float a1 = end.y - start.y;
    float b1 = start.x - end.x;
    float c1 = a1 * start.x + b1 * start.y;

    float a2 = relativePosition.y;
    float b2 = -relativePosition.x;
                    
    float determinant = (a1 * b2) - (a2 * b1);

    float x = (b2 * c1) / determinant;
    float y = -(a2 * c1) / determinant;
                    
    float2 intersection = float2(x, y);
    //float success = 0;
    //intersection = GetLineIntersection(start, end, revealerData.revealerPosition, revealerData.revealerPosition + relativePosition, success);

    return intersection;
}

#ifdef FOW_BLEED_ON
void CalculateFogBleed(float2 start, float2 end, float2 relativePosition, float2 intersection, inout float DistToSegmentEnd)
{
    float2 rotPoint = (start + end) / 2;
    float2 arcOrigin = rotPoint + (float2(-(end.y - rotPoint.y), (end.x - rotPoint.x)) * 3);
    float arcLength = distance(start, arcOrigin);
    float2 newRelativePosition = arcOrigin + normalize(relativePosition - arcOrigin) * arcLength;
    DistToSegmentEnd += distance(intersection, newRelativePosition) / 2;
}
#endif

int2 GetCell(float2 position)
{
    return int2(floor(position / _CellSize));
}

int GetCellHash(int2 cell)
{
    uint h = (uint)cell.x * 73856093u ^ (uint)cell.y * 19349663u;
    return h % _TableSize;
}

#if FOW_HARD

void LoopRevealerHardFog(RevealerInfoStruct revealerInfo, RevealerDataStruct revealerData, float2 relativePosition, float distToRevealerOrigin, int numSegments, float totalRevealerRadius, inout float RevealerOut)
{
    float2 toPixelDir = relativePosition / distToRevealerOrigin;
    
    int baseIndex = revealerInfo.startIndex;
    RevealerSightSegment previousCone = _SightSegmentBuffer[baseIndex];
    float crossPrev = toPixelDir.x * previousCone.segmentDirection.y - toPixelDir.y * previousCone.segmentDirection.x;
    //bool cutShortPrev = previousCone.length <= totalRevealerRadius;
    
    for (int c = 1; c < numSegments; c++)
    {
        RevealerSightSegment currentCone = _SightSegmentBuffer[baseIndex + c];
        
        float crossCurr = toPixelDir.x * currentCone.segmentDirection.y - toPixelDir.y * currentCone.segmentDirection.x;
        //bool cutShortCurr = currentCone.length <= totalRevealerRadius;

        bool inCone = (crossPrev <= 0) && (crossCurr >= 0);
        
        if (inCone)
        {
            bool cutShortCurr = currentCone.length <= totalRevealerRadius;
            float DistToSegmentEnd = currentCone.length + (cutShortCurr ? _extraRadius : 0);

            //if (previousCone.cutShort && currentCone.cutShort)
            bool cutShortPrev = previousCone.length <= totalRevealerRadius;
            if (cutShortPrev && cutShortCurr)
            {
                float2 start = previousCone.segmentDirection * previousCone.length;
                float2 end = currentCone.segmentDirection * currentCone.length;
                float distSq = dot(end - start, end - start);

                const float reqDistSq = 0.0225; // 0.15^2
                if (distSq > reqDistSq)
                {
                    float2 intersection = CalculateIntersectionCramersRule(start, end, relativePosition);
                    DistToSegmentEnd = length(intersection);
                    DistToSegmentEnd += _extraRadius;

                    #ifdef FOW_BLEED_ON
                    CalculateFogBleed(start, end, relativePosition, intersection, DistToSegmentEnd);
                    #endif
                }
            }
            
            DistToSegmentEnd = max(DistToSegmentEnd, revealerInfo.unobscuredRadius);
                    
            if (distToRevealerOrigin < DistToSegmentEnd)
            {
                RevealerOut = 1;
                return;
            }
        }

        crossPrev = crossCurr;
        //cutShortPrev = cutShortCurr;
        previousCone = currentCone;
    }
}

void FOW_Hard(float2 Position, float height, out float Out)
{
    Out = 0;
#if !FOW_SAMPLE_REALTIME && !defined(IS_FOW_RENDERTEXTURE)
    return;
#endif

    if (_pixelateWS)
    {
        Position *= _pixelDensity;
        Position -= _pixelOffset;
        Position = round(Position);
        Position += _pixelOffset;
        Position /= _pixelDensity;
    }

    #if FOW_USE_SPATIAL_HASHING
    int2 cell = GetCell(Position);
    int hash = GetCellHash(cell);
    int2 range = _GridRanges[hash];
    for (int i = range.x; i < range.y; i++)
    {
        int revealerIndex = _RevealerGridIds[i];
#else
    for (int i = 0; i < _NumRevealers; i++)
    {
        int revealerIndex = _ActiveRevealerIndices[i];
#endif
        RevealerDataStruct revealerData = _RevealerDataBuffer[revealerIndex];
        float2 relativePosition = Position - revealerData.revealerPosition;
    #if (FOW_PIXELATE) 
        relativePosition *= _pixelDensity;
        relativePosition = round(relativePosition);
        relativePosition /= _pixelDensity;
    #endif

        float sqDistToRevealerOrigin = dot(relativePosition, relativePosition);

        if (sqDistToRevealerOrigin > revealerData.totalRevealerRadius * revealerData.totalRevealerRadius)
            continue;

        RevealerInfoStruct revealerInfo = _RevealerInfoBuffer[revealerIndex];
        float distToRevealerOrigin = sqrt(sqDistToRevealerOrigin);

#if defined(IS_FOW_RENDERTEXTURE)
        float heightDistance = 0;
#else
        float heightDistance = abs(height - revealerData.revealerHeight);

        if (heightDistance > revealerInfo.visionHeight)
            continue;
#endif

        if (revealerInfo.unobscuredRadius < 0 && distToRevealerOrigin < -revealerInfo.unobscuredRadius)     //negative unobscured radius
            continue;

        float RevealerOut = 0;

        int numSegments = revealerData.numSegments;
        if (numSegments == 0)  //special condition - revealer has no occlusion, and is full circle
        {
            RevealerOut = 1;
        }
        else
        {
            if (distToRevealerOrigin < revealerInfo.unobscuredRadius)
                RevealerOut = 1;
            else
                LoopRevealerHardFog(revealerInfo, revealerData, relativePosition, distToRevealerOrigin, numSegments, revealerData.totalRevealerRadius, RevealerOut);
        }

        RevealerOut *= revealerInfo.revealerOpacity;
        if (BLEND_MAX)
            Out = max(Out, RevealerOut);
        else
            Out = min(1, Out + RevealerOut);

        if (IsOne(Out)) 
            return;
    }
}

#endif

float cross2(float2 a, float2 b)
{
    return a.x * b.y - a.y * b.x;
}

// 1. Helper for 2D Cross Product (Determinant)
// half precision is sufficient here as long as coordinates aren't massive.
inline half CrossProduct2D(half2 a, half2 b)
{
    return (a.x * b.y) - (a.y * b.x);
}

// 2. The Intersection Function
// Returns: The intersection point.
// Output Parameter 'success': 1.0 if intersection found, 0.0 if parallel.
half2 GetLineIntersection(half2 p1, half2 p2, half2 p3, half2 p4, out half success)
{
    half2 dirA = p2 - p1;
    half2 dirB = p4 - p3;
    half2 diff = p3 - p1;

    // Calculate the determinant (denominator)
    half det = CrossProduct2D(dirA, dirB);

    // UNITY SPECIFIC EPSILON
    // 'half' precision is rough. 1e-4 is standard for float, 
    // but 1e-2 or 1e-3 is safer for half to prevent huge number explosions.
    const half EPSILON = 1e-3;

    // Check if lines are parallel
    // We use abs() to check both positive and negative nearness to zero
    if (abs(det) < EPSILON)
    {
        success = 0.0;
        return half2(0, 0); // Return zero or a safe fallback
    }

    // Calculate t (distance factor along Line A)
    // t = Cross(p3-p1, dirB) / Cross(dirA, dirB)
    half t = CrossProduct2D(diff, dirB) / det;

    success = 1.0;
    
    // Result = p1 + t * (p2 - p1)
    return p1 + (dirA * t);
}

half2 ClosestPointOnLineSegment2D(half2 T_P, half2 T_A, half2 T_B)
{
    // 1. Calculate direction vector D (Segment vector)
    half2 D = T_B - T_A;
    
    // 2. Calculate offset vector V (Vector from A to P)
    half2 V = T_P - T_A;
    
    // Calculate the squared length of the segment vector (D . D)
    half sqrMagnitudeD = dot(D, D);

    // Handle the edge case where A and B are the same point (zero length segment)
    // If the length is near zero, return the start point A.
    if (sqrMagnitudeD < 1.0e-6)
    {
        return T_A; 
    }
    
    // 3. Calculate the projection ratio (t)
    // t = (V . D) / (D . D)
    half t = dot(V, D) / sqrMagnitudeD;

    // 4. Clamping: Restrict t to the [0, 1] range to stay on the segment
    // This is what makes it a 'segment' check, not an infinite line check.
    half t_clamped = clamp(t, (half)0.0, (half)1.0);

    // 5. Calculate the final closest point
    // P_C = T_A + D * t_clamped
    return T_A + (D * t_clamped);
}

#if FOW_SOFT

void LoopRevealerSoftFog(RevealerInfoStruct revealerInfo, RevealerDataStruct revealerData, float2 relativePosition, float distToRevealerOrigin, int numSegments, float totalRevealerRadius, inout float RevealerOut)
{
    float2 toPixelDir = relativePosition / distToRevealerOrigin;

    int baseIndex = revealerInfo.startIndex;
    RevealerSightSegment previousCone = _SightSegmentBuffer[baseIndex];
    float crossPrev = toPixelDir.x * previousCone.segmentDirection.y - toPixelDir.y * previousCone.segmentDirection.x;  // cross(toPixel, prev)
    //bool cutShortPrev = previousCone.length <= totalRevealerRadius;

//#if INNER_SOFTEN
    //float fadeThreshold = sin(radians(_fadeOutDegrees));
    float fadeThreshold = revealerInfo.innerSoftenThreshold;
//#endif
    
    for (int c = 1; c < numSegments; c++)
    {
        RevealerSightSegment currentCone = _SightSegmentBuffer[baseIndex + c];
        
        float crossCurr = toPixelDir.x * currentCone.segmentDirection.y - toPixelDir.y * currentCone.segmentDirection.x;
        //bool cutShortCurr = currentCone.length <= totalRevealerRadius;

        bool inCone = (crossPrev <= 0) && (crossCurr >= 0);

//#if INNER_SOFTEN
        bool inAngularSoftenZone = false;
        if (!inCone) 
        { 
            float2 midDirUnnorm = previousCone.segmentDirection + currentCone.segmentDirection;
            float dotMidUnnorm = dot(toPixelDir, midDirUnnorm);
            bool facingSegment = dotMidUnnorm > 0;
            if (facingSegment) 
            { 
                bool nearCurrEdge = (crossCurr < 0) && (crossCurr > -fadeThreshold);
                bool nearPrevEdge = (crossPrev > 0) && (crossPrev < fadeThreshold);
                inAngularSoftenZone = nearCurrEdge || nearPrevEdge;
            }
        }

        if (inCone || inAngularSoftenZone)
//#else
//        if (inCone)
//#endif
        {
            float _fadeOutDistance = max(0, revealerInfo.revealerFadeDistance);

            //float lerpVal = 1-saturate(-signedDeltaAngle / segmentAngle);
            //float DistToSegmentEnd = lerp(previousCone.length, currentCone.length, lerpVal);
            float currConeLength = min(totalRevealerRadius, currentCone.length);
            bool cutShortCurr = currentCone.length <= totalRevealerRadius;
            float DistToSegmentEnd = currConeLength + (cutShortCurr ? _extraRadius : 0);
            //float segmentSoftenDistance = _fadeOutDistance;

            float innerEdgeMultiplier = 1;
//#if INNER_SOFTEN

            if (inAngularSoftenZone && !inCone)
            {
                //if (nearPrevEdge && !nearCurrEdge)  // If crossCurr is the issue (we're outside curr edge)  
                //{
                //    innerEdgeMultiplier = 1 - (crossPrev / fadeThreshold);
                //}
                //else if (nearCurrEdge && !nearPrevEdge) // If crossCurr is the issue (we're outside curr edge)  
                //{
                //    innerEdgeMultiplier = 1 - (-crossCurr / fadeThreshold);
                //}
                //else    // Near both edges - take the minimum
                //{
                //    float prevFade = 1 - (crossPrev / fadeThreshold);
                //    float currFade = 1 - (-crossCurr / fadeThreshold);
                //    innerEdgeMultiplier = min(prevFade, currFade);
                //}
                //innerEdgeMultiplier = saturate(innerEdgeMultiplier);
                float prevFade = 1 - crossPrev * revealerInfo.invInnerSoftenThreshold;
                float currFade = 1 + crossCurr * revealerInfo.invInnerSoftenThreshold;
                innerEdgeMultiplier = saturate(min(prevFade, currFade));
            }
            //innerEdgeMultiplier = 1;
//#endif

            float RadialEdgeSoftening = 1;
            //if ((previousCone.cutShort && currentCone.cutShort))  //draw straight line thru points instead of drawing arc
            bool cutShortPrev = previousCone.length <= totalRevealerRadius;
            if ((cutShortPrev && cutShortCurr))  //draw straight line thru points instead of drawing arc
            {
                float prevConeLength = min(totalRevealerRadius, previousCone.length);

                float2 start = previousCone.segmentDirection * prevConeLength;
                float2 end = currentCone.segmentDirection * currConeLength;
                float distSq = dot(end - start, end - start);

                //const float reqDist = .03509946;
                //const float reqDist = .75;
                const float reqDist = .15;
                if (distSq > reqDist * reqDist) //super small angle segments do not have enough precision and can result in leaks.
                {
                    //start and end are local to the revealers position
                    float2 intersection = CalculateIntersectionCramersRule(start, end, relativePosition);
                    
                    DistToSegmentEnd = length(intersection);
                //#if INNER_SOFTEN
                    DistToSegmentEnd = min(max(prevConeLength, currConeLength), DistToSegmentEnd);
                //#endif
                    DistToSegmentEnd += _extraRadius;

                    #ifdef FOW_BLEED_ON
                    CalculateFogBleed(start, end, relativePosition, intersection, DistToSegmentEnd);
                    #endif
                }

                RadialEdgeSoftening = CalculateFadeZonePercent(_extraRadius, _edgeSoftenDistance, distToRevealerOrigin - DistToSegmentEnd);
                        
                //segmentSoftenDistance = 0;
                //if (DistToSegmentEnd > revealerInfo.revealerRadius)
                //{
                //    segmentSoftenDistance = max(0, DistToSegmentEnd - revealerInfo.revealerRadius);
                //    DistToSegmentEnd = revealerInfo.revealerRadius;
                //}
                //segmentSoftenDistance += _edgeSoftenDistance;
                
            }

            //segmentSoftenDistance = min(segmentSoftenDistance, revealerInfo.revealerRadius + _fadeOutDistance);
            DistToSegmentEnd = max(DistToSegmentEnd, revealerInfo.unobscuredRadius);
            DistToSegmentEnd = min(DistToSegmentEnd, revealerInfo.revealerRadius);

            if (distToRevealerOrigin < DistToSegmentEnd + _fadeOutDistance)
            {
                float revVal = SmoothValue(CalculateFadeZonePercent(DistToSegmentEnd, _fadeOutDistance, distToRevealerOrigin));
                revVal*=RadialEdgeSoftening;
                revVal*=SmoothValue(innerEdgeMultiplier);
                RevealerOut = max(RevealerOut, revVal);
            }
            if (IsOne(RevealerOut)) 
                return;
        }

        crossPrev = crossCurr;
        //cutShortPrev = cutShortCurr;
        previousCone = currentCone;
    }
}

void FOW_Soft(float2 Position, float height, out float Out)
{
    Out = 0;
#if !FOW_SAMPLE_REALTIME && !defined(IS_FOW_RENDERTEXTURE)
    return;
#endif

    if (_pixelateWS)
    {
        Position *= _pixelDensity;
        Position -= _pixelOffset;
        Position = round(Position);
        Position += _pixelOffset;
        Position /= _pixelDensity;
    }

#if FOW_USE_SPATIAL_HASHING
    int2 cell = GetCell(Position);
    int hash = GetCellHash(cell);
    int2 range = _GridRanges[hash];
    for (int i = range.x; i < range.y; i++)
    {
        int revealerIndex = _RevealerGridIds[i];
#else
    for (int i = 0; i < _NumRevealers; i++)
    {
        int revealerIndex = _ActiveRevealerIndices[i];
#endif
        
        RevealerDataStruct revealerData = _RevealerDataBuffer[revealerIndex];
        float2 relativePosition = Position - revealerData.revealerPosition;
    #if FOW_PIXELATE
        relativePosition *= _pixelDensity;
        relativePosition = round(relativePosition);
        relativePosition /= _pixelDensity;
    #endif

        float maxPossibleDistance = revealerData.totalRevealerRadius;

        float sqDistToRevealerOrigin = dot(relativePosition, relativePosition);

        if (sqDistToRevealerOrigin > maxPossibleDistance * maxPossibleDistance)
            continue;

        RevealerInfoStruct revealerInfo = _RevealerInfoBuffer[revealerIndex];
        float distToRevealerOrigin = sqrt(sqDistToRevealerOrigin);
        
        float RevealerOut = 0;

    #if defined(IS_FOW_RENDERTEXTURE)
        float heightMultiplier = 1;
    #else
        float heightDistance = abs(height - revealerData.revealerHeight);
        float maxHeightDist = revealerInfo.visionHeight + revealerInfo.heightFade;

        float heightFade = saturate(1 - (heightDistance - revealerInfo.visionHeight) / revealerInfo.heightFade);

        float heightMultiplier = (heightDistance <= revealerInfo.visionHeight) ? 1.0 : heightFade;

        if (heightMultiplier <= 0)
            continue;
    #endif

        if (revealerInfo.unobscuredRadius < 0)    //negative unobscured radius
        {
            if (distToRevealerOrigin < -revealerInfo.unobscuredRadius + revealerInfo.unobscuredSoftenRadius)
            {
                if (distToRevealerOrigin < -revealerInfo.unobscuredRadius)
                    continue;
                //Out = max(Out, heightMultiplier * lerp(1, 0, (distToRevealerOrigin - revealerInfo.unobscuredRadius) / revealerInfo.unobscuredSoftenRadius));
                heightMultiplier *= (distToRevealerOrigin - -revealerInfo.unobscuredRadius) / revealerInfo.unobscuredSoftenRadius;
            }
        }

        int numSegments = revealerData.numSegments;
        if (numSegments == 0)  //special condition - revealer has no occlusion, and is full circle
        { 
            RevealerOut = max(RevealerOut, SmoothValue(CalculateFadeZonePercent(revealerInfo.revealerRadius, revealerInfo.revealerFadeDistance, distToRevealerOrigin)));
        }
        else
        {
            if (distToRevealerOrigin < revealerInfo.unobscuredRadius)
                RevealerOut = 1;
            else
            {
                LoopRevealerSoftFog(revealerInfo, revealerData, relativePosition, distToRevealerOrigin, numSegments, revealerData.totalRevealerRadius, RevealerOut);
                if (distToRevealerOrigin < revealerInfo.unobscuredRadius + revealerInfo.unobscuredSoftenRadius)
                    RevealerOut = max(RevealerOut, CalculateFadeZonePercent(revealerInfo.unobscuredRadius, revealerInfo.unobscuredSoftenRadius, distToRevealerOrigin));
            }

            //if (distToRevealerOrigin < revealerInfo.unobscuredRadius + revealerInfo.unobscuredSoftenRadius)
            //{
            //    RevealerOut = max(RevealerOut, CalculateFadeZonePercent(revealerInfo.unobscuredRadius, revealerInfo.unobscuredSoftenRadius, distToRevealerOrigin));
            //}
            //if (!IsOne(RevealerOut))
            //    LoopRevealerSoftFog(revealerInfo, revealerData, relativePosition, distToRevealerOrigin, RevealerOut);
        }

        RevealerOut *= heightMultiplier;

        RevealerOut *= revealerInfo.revealerOpacity;
        if (BLEND_MAX)
            Out = max(Out, RevealerOut);
        else
            Out = min(1, Out + RevealerOut);
            
        if (IsOne(Out))
            break;
    }
    
    #ifdef FOW_DITHER_ON
    Dither(Out, abs(Position + float2(5000, 5000)), Out);
    #endif
}

#endif

float2 closestPointOnLine(float2 p1, float2 p2, float2 pnt)
{
    float2 direction = normalize(p1 - p2);
    float2 vec = pnt - p1;
    float dst = dot(vec, direction);
    return p1 + direction * dst;
}

void FOW_Outline_float(float2 Position, float height, out float Out)
{
    Out = 0;
//#if FOW_SAMPLE_REALTIME
//#else
//    return;
//#endif
    
//    for (int i = 0; i < _NumRevealers; i++)
//    {
//        RevealerInfoStruct revealerInfo = _RevealerInfoBuffer[_ActiveRevealerIndices[i]];
//        RevealerDataStruct revealerData = _RevealerDataBuffer[_ActiveRevealerIndices[i]];
//        float distToRevealerOrigin = distance(Position, revealerData.revealerPosition);
//        if (distToRevealerOrigin < revealerInfo.revealerRadius + lineThickness)
//        {
//#if defined(IS_FOW_RENDERTEXTURE)
//            float heightDist = 0;
//#else
//            float heightDist = abs(height - revealerData.revealerHeight);
//#endif

//            if (heightDist > revealerInfo.visionHeight)
//                continue;

////#if MIN_DIST_ON
//            if (revealerInfo.unobscuredRadius < 0 && distToRevealerOrigin < -revealerInfo.unobscuredRadius)
//                continue;
////#endif

//            float2 relativePosition = Position - revealerData.revealerPosition;
//            float deg = degrees(atan2(relativePosition.y, relativePosition.x));
            
//            RevealerSightSegment previousCone = _SightSegmentBuffer[revealerInfo.startIndex];
//            //float prevAng = previousCone.edgeAngle - .001;
//            //float prevAng = previousCone.edgeAngle + .01;
//            float prevAng = previousCone.edgeAngle;
//            for (int c = 0; c < revealerData.numSegments; c++)
//            {
//                //prevAng = previousCone.edgeAngle - .001;
//                //prevAng = previousCone.edgeAngle + .01;
//                prevAng = previousCone.edgeAngle;
//                RevealerSightSegment currentCone = _SightSegmentBuffer[revealerInfo.startIndex + c];

//                if (previousCone.cutShort != currentCone.cutShort)
//                {
//                    float2 previousPoint = revealerData.revealerPosition + float2(previousCone.length * cos(radians(previousCone.edgeAngle)), previousCone.length * sin(radians(previousCone.edgeAngle)));
//                    float2 currentPoint = revealerData.revealerPosition + float2(currentCone.length * cos(radians(currentCone.edgeAngle)), currentCone.length * sin(radians(currentCone.edgeAngle)));
                
//                    float len = distance(previousPoint, currentPoint) + lineThickness;
//                    float dstTop1 = distance(previousPoint, Position);
//                    float dstTop2 = distance(currentPoint, Position);
                
//                    float2 ClosestPointOnLine = closestPointOnLine(previousPoint, currentPoint, Position);
//                    float dstToLine = distance(ClosestPointOnLine, Position);
                
//                //dst = distance(currentPoint, Position);
//                    if (dstToLine < lineThickness && dstTop1 < len && dstTop2 < len)
//                    {
//                        Out = 1;
//                        return;
//                    }
//                }
//                float degDiff = angleDiff(deg + 360, currentCone.edgeAngle);
//                float segmentAngle = currentCone.edgeAngle - prevAng;
                
//                //if (deg > prevAng && currentCone.edgeAngle > deg)
//                if (degDiff > -segmentAngle && degDiff < 0)
//                {
//                    //float lerpVal = (deg - prevAng) / (currentCone.edgeAngle - prevAng);
//                    //float DistToSegmentEnd = lerp(previousCone.length, currentCone.length, lerpVal);
//                    float DistToSegmentEnd = currentCone.length;
//                    //if (abs(previousCone.length - revealerInfo.revealerRadius) > .001 || abs(currentCone.length - revealerInfo.revealerRadius) > .001)
//                    if (previousCone.cutShort && currentCone.cutShort)
//                    {
//                        float2 start = revealerData.revealerPosition + float2(cos(radians(prevAng)), sin(radians(prevAng))) * previousCone.length;
//                        float2 end = revealerData.revealerPosition + float2(cos(radians(currentCone.edgeAngle)), sin(radians(currentCone.edgeAngle))) * currentCone.length;
                        
//                        float a1 = end.y - start.y;
//                        float b1 = start.x - end.x;
//                        float c1 = a1 * start.x + b1 * start.y;
                    
//                        float a2 = Position.y - revealerData.revealerPosition.y;
//                        float b2 = revealerData.revealerPosition.x - Position.x;
//                        float c2 = a2 * revealerData.revealerPosition.x + b2 * revealerData.revealerPosition.y;
                    
//                        float determinant = (a1 * b2) - (a2 * b1);
                    
//                        float x = (b2 * c1 - b1 * c2) / determinant;
//                        float y = (a1 * c2 - a2 * c1) / determinant;
                    
//                        float2 intercection = float2(x, y);
//                        DistToSegmentEnd = distance(intercection, revealerData.revealerPosition);
//                    }
//                    DistToSegmentEnd = max(DistToSegmentEnd, revealerInfo.unobscuredRadius);
                    
//                    if (distance(distToRevealerOrigin, DistToSegmentEnd) < lineThickness)
//                    {
//                        Out = 1;
//                        return;
//                    }
//                }
                
//                previousCone = currentCone;
//            }
//        }
//    }
}

//shadergraph rotate node
void FOW_Rotate_Degrees_float(float2 UV, float2 Center, float Rotation, out float2 Out)
{
    Rotation = Rotation * (3.1415926f / 180.0f);
    UV -= Center;
    float s = sin(Rotation);
    float c = cos(Rotation);
    float2x2 rMatrix = float2x2(c, -s, s, c);
    rMatrix *= 0.5;
    rMatrix += 0.5;
    rMatrix = rMatrix * 2 - 1;
    UV.xy = mul(UV.xy, rMatrix);
    UV += Center;
    Out = UV;
}

void FOW_Rotate_Degrees_half(half2 UV, half2 Center, half Rotation, out half2 Out)
{
    Rotation = Rotation * (3.1415926f / 180.0f);
    UV -= Center;
    half s = sin(Rotation);
    half c = cos(Rotation);
    half2x2 rMatrix = half2x2(c, -s, s, c);
    rMatrix *= 0.5;
    rMatrix += 0.5;
    rMatrix = rMatrix * 2 - 1;
    UV.xy = mul(UV.xy, rMatrix);
    UV += Center;
    Out = UV;
}

void FOW_Sample_Raw_float(float2 Position, float height, out float Out)
{
    Out = 0;
#if FOW_HARD
                FOW_Hard(Position, height, Out);
#elif FOW_SOFT
                FOW_Soft(Position, height, Out);
#endif
}

void FOW_Sample_Raw_half(half2 Position, half height, out half Out)
{
    Out = 0;
#if FOW_HARD
                FOW_Hard(Position, height, Out);
#elif FOW_SOFT
                FOW_Soft(Position, height, Out);
#endif
}

void FOW_Sample_float(float2 Position, float height, out float Out)
{
    FOW_Sample_Raw_float(Position, height, Out);
    CustomCurve_float(Out, Out);
    TextureSample_float(Position, Out);
    Out = lerp(1, Out, FowEffectStrength);
}

void FOW_Sample_half(half2 Position, half height, out half Out)
{
    Out = 0;
    FOW_Sample_Raw_half(Position, height, Out);
    CustomCurve_half(Out, Out);
    TextureSample_float(Position, Out);
    Out = lerp(1, Out, FowEffectStrength);
}

void GetFowSpacePosition(float3 PositionWS, out float2 PositionFS, out float height)
{
    [branch]
    switch (_fowPlane)
    {
        case 0: //2D
            PositionFS = PositionWS.xy;
            height = 0;
            return;
        case 1: //XZ
            PositionFS = PositionWS.xz;
            height = PositionWS.y;
            return;
        case 2: //XY
            PositionFS = PositionWS.xy;
            height = PositionWS.z;
            return;
        case 3: //ZY
            PositionFS = PositionWS.zy;
            height = PositionWS.x;
            return;
        default:
            PositionFS = PositionWS.xy;
            height = PositionWS.z;
            return;
    }
}

//used for partial hiders
void FOW_Sample_WS_float(float3 PositionWS, out float Out)
{
    float2 pos = float2(0, 0);
    float height = 0;
    GetFowSpacePosition(PositionWS, pos, height);
    FOW_Sample_float(pos, height, Out);
}

void FOW_Sample_WS_half(float3 PositionWS, out half Out)
{
    half2 pos = float2(0, 0);
    half height = 0;
    GetFowSpacePosition(PositionWS, pos, height);
    FOW_Sample_float(pos, height, Out);
}

#endif