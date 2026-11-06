// Agua barata para VR (Quest). Sin textura de profundidad, sin textura de pantalla, sin camara de reflejo.
// Costo por pixel: 2 muestras de textura + matematica simple.
//  - Olas: dos capas de un mapa de normales, en coordenadas de mundo (no dependen de la UV de la malla).
//  - Reflejo: degradado del cielo (horizonte -> cenit) con Fresnel. Los colores los pone AmbienteVR segun el cielo activo.
//  - Brillo: reflejo de la luz principal (sol) sobre las olas.
//  - Lejos las olas se calman y el agua se funde con la niebla del horizonte.
//  - Sigue siendo transparente (mas opaca al mirar de lado) para que se vean los peces nadando bajo la superficie.
Shader "Llaneritos/Agua VR"
{
    Properties
    {
        [Header(Olas)]
        [NoScaleOffset] _NormalMap ("Normales de las olas", 2D) = "bump" {}
        _EscalaOlas ("Metros por repeticion de la textura", Float) = 7
        _VelocidadOlas ("Velocidad", Float) = 0.035
        _FuerzaOlas ("Fuerza de las olas", Range(0, 1.5)) = 0.55
        _DistanciaOlas ("Distancia a la que se calman (m)", Float) = 160

        [Header(Color del agua)]
        _ColorProfundo ("Mirando hacia abajo", Color) = (0.02, 0.07, 0.11, 1)
        _ColorSuperficial ("De lado (poco profunda)", Color) = (0.07, 0.22, 0.24, 1)
        _Opacidad ("Opacidad mirando hacia abajo", Range(0, 1)) = 0.92

        [Header(Reflejo del cielo)]
        _ColorHorizonte ("Cielo en el horizonte", Color) = (0.8, 0.6, 0.3, 1)
        _ColorMedio ("Cielo a media altura", Color) = (0.85, 0.7, 0.5, 1)
        _ColorZenit ("Cielo arriba", Color) = (0.2, 0.3, 0.55, 1)
        _ReflejoMinimo ("Reflejo minimo", Range(0, 1)) = 0.12
        _FresnelPotencia ("Fresnel (potencia)", Range(1, 8)) = 3

        [Header(Brillo del sol)]
        _SolIntensidad ("Intensidad", Float) = 1.4
        _SolDureza ("Dureza", Float) = 70
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);

            CBUFFER_START(UnityPerMaterial)
                half _EscalaOlas;
                half _VelocidadOlas;
                half _FuerzaOlas;
                half _DistanciaOlas;
                half4 _ColorProfundo;
                half4 _ColorSuperficial;
                half _Opacidad;
                half4 _ColorHorizonte;
                half4 _ColorMedio;
                half4 _ColorZenit;
                half _ReflejoMinimo;
                half _FresnelPotencia;
                half _SolIntensidad;
                half _SolDureza;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half fogFactor : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs v = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = v.positionCS;
                output.positionWS = v.positionWS;
                output.fogFactor = ComputeFogFactor(v.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 haciaCamara = _WorldSpaceCameraPos - input.positionWS;
                float dist = length(haciaCamara);
                half3 V = (half3)(haciaCamara / max(dist, 0.0001));

                // Olas: dos capas moviendose en direcciones distintas
                float t = _Time.y * _VelocidadOlas;
                float2 p = input.positionWS.xz / _EscalaOlas;
                half3 n1 = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, p + float2(t, t * 0.6)));
                half3 n2 = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, p * 0.57 + float2(-t * 0.8, t * 0.45) + 0.37));
                half calma = saturate(1.0 - dist / _DistanciaOlas);          // lejos: agua lisa (menos parpadeo, mas horizonte limpio)
                half2 desvio = (n1.xy + n2.xy) * (_FuerzaOlas * calma);
                half3 N = normalize(half3(desvio.x, 1.0, desvio.y));

                // Fresnel: de lado refleja el cielo, hacia abajo se ve el agua
                half NoV = saturate(dot(N, V));
                half fres = saturate(_ReflejoMinimo + (1.0 - _ReflejoMinimo) * pow(1.0 - NoV, _FresnelPotencia));

                half3 R = reflect(-V, N);
                // Degradado del cielo en tres tramos: horizonte -> media altura (hasta ~24 grados) -> cenit
                half altura = saturate(R.y);
                half3 cielo = lerp(lerp(_ColorHorizonte.rgb, _ColorMedio.rgb, saturate(altura / 0.4)), _ColorZenit.rgb, saturate((altura - 0.4) / 0.6));
                half3 cuerpo = lerp(_ColorSuperficial.rgb, _ColorProfundo.rgb, NoV);

                // Brillo del sol sobre las olas
                Light luz = GetMainLight();
                half brillo = pow(saturate(dot(R, luz.direction)), _SolDureza) * _SolIntensidad;

                half3 color = lerp(cuerpo, cielo, fres) + luz.color * brillo;
                half alfa = saturate(lerp(_Opacidad, 1.0, fres) + brillo);

                color = MixFog(color, input.fogFactor);
                return half4(color, alfa);
            }
            ENDHLSL
        }
    }
}
