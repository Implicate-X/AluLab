# Copilot Instructions

## General Guidelines
- Bei vielen Änderungen/Versuchen und entstehenden Regressionen zuerst auf einen stabilen Stand (z.B. von vorgestern) zurückgehen und die Lösung (Token/Ready-Handshake) anschließend in kleinen, sauberen Schritten erneut implementieren. Wenn eine zuvor vorgeschlagene Implementierung trotz korrekter Theorie nicht funktioniert, soll ebenfalls auf den letzten stabilen Stand zurückgegangen und dann schrittweise neu aufgebaut werden, anstatt viele inkrementelle Korrekturen vorzunehmen.
- Bei Touch-Problemen keine Änderungen am Touch-Mapping vornehmen, wenn der Nutzer bestätigt, dass Touch korrekt ausgerichtet funktioniert; Fokus dann nur auf Display-Inhalt-Verschiebung.

## Code Requirements
- In Display-Spiegel-Code immer die Usings `using Iot.Device.Graphics;` und `using Iot.Device.Graphics.SkiaSharpAdapter;` enthalten sein, da diese sonst manuell nachgetragen werden müssen.