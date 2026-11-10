// AviX Mirror — LEDs WS2812B sur Arduino Nano (premiers rétros AVIX_3D).
// Installé par AviX Mirror (onglet LEDs → « Programmer l'Arduino »), ou avec l'Arduino IDE
// (carte « Arduino Nano », bibliothèque « Adafruit NeoPixel »).
//
// Protocole série, 115200 bauds, chaque commande commence par l'octet 0xA5 :
//   A5 'H'                       → répond « AVIXLED1\n » (identification)
//   A5 'P' broche                → broche des LEDs (2 à 13, D2 par défaut), LEDs éteintes
//   A5 'L' n R G B … somme       → n couleurs (somme = XOR de n et des données), répond 'K' après affichage
// Sans nouvelle commande pendant 5 s (application fermée, câble débranché), les LEDs s'éteignent.

#include <Adafruit_NeoPixel.h>

const uint8_t MaxLeds = 128;
const uint8_t DefaultPin = 2;
const unsigned long IdleTimeoutMs = 5000;

Adafruit_NeoPixel strip(16, DefaultPin, NEO_GRB + NEO_KHZ800);

enum State : uint8_t { WaitHeader, WaitCommand, WaitPin, WaitCount, WaitData, WaitChecksum };
State state = WaitHeader;
uint8_t count = 0, checksum = 0;
uint16_t received = 0;
uint8_t data[MaxLeds * 3];
unsigned long lastCommand = 0;
bool lit = false;

void clearLeds() {
  strip.clear();
  strip.show();
  lit = false;
}

void setup() {
  Serial.begin(115200);
  strip.begin();
  clearLeds();
}

void handle(uint8_t b) {
  switch (state) {
    case WaitHeader:
      if (b == 0xA5) state = WaitCommand;
      break;
    case WaitCommand:
      lastCommand = millis();
      if (b == 'H') { Serial.print(F("AVIXLED1\n")); state = WaitHeader; }
      else if (b == 'P') state = WaitPin;
      else if (b == 'L') state = WaitCount;
      else state = (b == 0xA5) ? WaitCommand : WaitHeader;
      break;
    case WaitPin:
      if (b >= 2 && b <= 13) {
        strip.clear();
        strip.show();
        strip.setPin(b);
        strip.begin();
        clearLeds();
      }
      state = WaitHeader;
      break;
    case WaitCount:
      count = b > MaxLeds ? MaxLeds : b;
      checksum = b;
      received = 0;
      state = count == 0 ? WaitChecksum : WaitData;
      break;
    case WaitData:
      data[received++] = b;
      checksum ^= b;
      if (received >= (uint16_t)count * 3) state = WaitChecksum;
      break;
    case WaitChecksum:
      if (b == checksum) {
        if (strip.numPixels() != count) strip.updateLength(count);
        for (uint8_t i = 0; i < count; i++)
          strip.setPixelColor(i, data[i * 3], data[i * 3 + 1], data[i * 3 + 2]);
        strip.show();
        lit = true;
      }
      // Réponse après affichage : l'application attend ce 'K' avant d'envoyer la suite
      // (les octets reçus pendant show() seraient perdus).
      Serial.write('K');
      state = WaitHeader;
      break;
  }
}

void loop() {
  while (Serial.available() > 0)
    handle((uint8_t)Serial.read());
  if (lit && millis() - lastCommand > IdleTimeoutMs)
    clearLeds();
}
