#include <SPI.h>
#include <MFRC522.h>
#include <Servo.h>

const int PIN_VRX = A0;
const int PIN_VRY = A1;
const int PIN_SLD = A2;
const int PIN_LDR = A3;
const int PIN_SW  = 4;

const int PIN_TRIG = 7;
const int PIN_ECHO = 8;

const int PIN_RST = 9;
const int PIN_SS  = 10;

const int PIN_SERVO = 6;

// --- ENCODEUR KY-040 ---
const int PIN_ENC_CLK = 2;   // interrupt 0
const int PIN_ENC_DT  = 3;   // interrupt 1
const int PIN_ENC_SW  = 5;   // bouton de l'encodeur (pullup)

MFRC522 rfid(PIN_SS, PIN_RST);
Servo servo;

const int SERVO_NEUTRAL = 90;

String bufferSerie = "";

const unsigned long ECHO_TIMEOUT_US = 20000UL;
const int DIST_MIN_CM = 3;
const int DIST_MAX_CM = 20;

// --- État encodeur (volatile car modifié en ISR) ---
volatile long encoderCount = 0;
volatile uint8_t lastEncState = 0;

// Table de transition quadrature : index = (ancien<<2) | nouveau
// Valeurs : +1 / -1 / 0 selon la transition (table standard Gray code)
const int8_t ENC_TABLE[16] = {
  0, -1,  1,  0,
  1,  0,  0, -1,
 -1,  0,  0,  1,
  0,  1, -1,  0
};

void encoderISR() {
  uint8_t clk = digitalRead(PIN_ENC_CLK);
  uint8_t dt  = digitalRead(PIN_ENC_DT);
  uint8_t newState = (clk << 1) | dt;
  uint8_t idx = (lastEncState << 2) | newState;
  encoderCount += ENC_TABLE[idx];
  lastEncState = newState;
}

long lireDistanceBrute() {
  digitalWrite(PIN_TRIG, LOW);
  delayMicroseconds(2);
  digitalWrite(PIN_TRIG, HIGH);
  delayMicroseconds(10);
  digitalWrite(PIN_TRIG, LOW);

  unsigned long duree = pulseIn(PIN_ECHO, HIGH, ECHO_TIMEOUT_US);
  if (duree == 0) return -1;
  long cm = (long)(duree / 58);
  if (cm < DIST_MIN_CM || cm > DIST_MAX_CM) return -1;
  return cm;
}

long lireDistanceFiltree() {
  long a = lireDistanceBrute();
  long b = lireDistanceBrute();
  long c = lireDistanceBrute();
  if (a < 0 || b < 0 || c < 0) return -1;
  if ((a <= b && b <= c) || (c <= b && b <= a)) return b;
  if ((b <= a && a <= c) || (c <= a && a <= b)) return a;
  return c;
}

void traiterCommande(const String& cmd) {
  if (cmd.startsWith("SERVO:")) {
    int angle = cmd.substring(6).toInt();
    angle = constrain(angle, 0, 180);
    servo.write(angle);
  }
}

void lireSerie() {
  while (Serial.available() > 0) {
    char c = Serial.read();
    if (c == '\n' || c == '\r') {
      if (bufferSerie.length() > 0) {
        traiterCommande(bufferSerie);
        bufferSerie = "";
      }
    } else {
      bufferSerie += c;
      if (bufferSerie.length() > 64) bufferSerie = "";
    }
  }
}

void setup() {
  Serial.begin(115200);
  pinMode(PIN_SW, INPUT_PULLUP);
  pinMode(PIN_TRIG, OUTPUT);
  pinMode(PIN_ECHO, INPUT);
  digitalWrite(PIN_TRIG, LOW);

  // Encodeur
  pinMode(PIN_ENC_CLK, INPUT_PULLUP);
  pinMode(PIN_ENC_DT,  INPUT_PULLUP);
  pinMode(PIN_ENC_SW,  INPUT_PULLUP);
  lastEncState = (digitalRead(PIN_ENC_CLK) << 1) | digitalRead(PIN_ENC_DT);
  attachInterrupt(digitalPinToInterrupt(PIN_ENC_CLK), encoderISR, CHANGE);
  attachInterrupt(digitalPinToInterrupt(PIN_ENC_DT),  encoderISR, CHANGE);

  servo.attach(PIN_SERVO);
  servo.write(SERVO_NEUTRAL);

  SPI.begin();
  rfid.PCD_Init();
  delay(50);

  delay(200);            // laisse le buffer TX se vider
  Serial.flush();        // force l'envoi de tout ce qui reste
  Serial.println();      // saute une ligne au cas où
  Serial.println("READY");
  Serial.flush();
}

// Division par 4 : le KY-040 émet typiquement 4 transitions par cran.
// On renvoie le delta en "crans" depuis la dernière lecture.
long consommerEncoder() {
  noInterrupts();
  long c = encoderCount;
  encoderCount = 0;
  interrupts();
  // Diviser par 4 pour obtenir des crans "propres"
  return c / 4;
}

void loop() {
  lireSerie();

  int x   = analogRead(PIN_VRX);
  int y   = analogRead(PIN_VRY);
  int sld = analogRead(PIN_SLD);
  int ldr = analogRead(PIN_LDR);
  int btn = (digitalRead(PIN_SW) == LOW) ? 1 : 0;
  int encBtn = (digitalRead(PIN_ENC_SW) == LOW) ? 1 : 0;
  long encDelta = consommerEncoder();
  long dist = lireDistanceFiltree();

  Serial.print("X:");    Serial.print(x);
  Serial.print(" Y:");   Serial.print(y);
  Serial.print(" SLD:"); Serial.print(sld);
  Serial.print(" LDR:"); Serial.print(ldr);
  Serial.print(" BTN:"); Serial.print(btn);
  Serial.print(" ENC:"); Serial.print(encDelta);
  Serial.print(" ENCBTN:"); Serial.print(encBtn);
  Serial.print(" DIST:"); Serial.println(dist);

  if (rfid.PICC_IsNewCardPresent() && rfid.PICC_ReadCardSerial()) {
    Serial.print("RFID:");
    for (byte i = 0; i < rfid.uid.size; i++) {
      if (rfid.uid.uidByte[i] < 0x10) Serial.print("0");
      Serial.print(rfid.uid.uidByte[i], HEX);
    }
    Serial.println();
    rfid.PICC_HaltA();
    rfid.PCD_StopCrypto1();
  }

  delay(20);
}