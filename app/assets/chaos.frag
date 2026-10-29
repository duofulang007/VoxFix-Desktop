#version 120
uniform vec3 cameraPosition;

uniform float u_time;
uniform float u_frequency;
varying vec2 vUv;
varying vec3 vNormal;
varying float vDisplacement;

void main() {
    // Base color - Dark Cyan/Blue
    vec3 baseColor = vec3(0.0, 0.1, 0.2);
    
    // Highlight color - Bright Cyan
    vec3 highlightColor = vec3(0.0, 0.8, 1.0);
    
    // Mix based on displacement
    vec3 color = mix(baseColor, highlightColor, smoothstep(-0.2, 0.5, vDisplacement));
    
    // Add "wireframe" grid effect or glowing pulse
    float pulse = sin(u_time * 2.0) * 0.5 + 0.5;
    
    // Rim lighting (Fresnel effect)
    vec3 viewDirection = normalize(cameraPosition - vec3(0.0)); // Approximate
    float fresnel = pow(1.0 - dot(vNormal, vec3(0.0, 0.0, 1.0)), 2.0); // Simplified view dir
    
    color += highlightColor * fresnel * 0.5;
    
    // Audio reactive glow intensity
    color *= (1.0 + u_frequency * 0.01);

    gl_FragColor = vec4(color, 0.9);
}
