// Constantes matérielles partagées par tout ce qui parle à l'Arduino.
// Le firmware utilise analogRead() (ADC 10 bits) → plage brute 0..1023, centre 512.
// Les calibrations spécifiques (offset du slider, deadzone, LdrMin/Max) restent
// exportées par chaque script car elles dépendent du capteur physique installé.
public static class ArduinoConfig
{
	public const int AnalogMin = 0;
	public const int AnalogCenter = 512;
	public const int AnalogMax = 1023;
}
