attribute vec4 Position;
attribute vec4 SourceColor;
attribute vec2 TexCoordIn;
 
varying vec4 DestinationColor;
varying vec2 TexCoordOut;

uniform mat4 Projection;
uniform mat4 ModelView;
   
void main(void) {
    DestinationColor = SourceColor;
    gl_Position = Projection * ModelView * Position;
    TexCoordOut = TexCoordIn;
}
