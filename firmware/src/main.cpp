// Yaesu ACC spectrum, first version: a dumb pipe.
//
// The FTdx101MP sends its scope data out of the ACC socket as SPI: CS (pin 6)
// low for each frame, CLOCK (pin 5) at about 2 MHz, data on DATA2 (pin 4). This
// takes each CS-low period as one transfer, with the ESP32 as the SPI slave, and
// sends the bytes to the PC over the native USB (the socket marked USB),
// unchanged, behind an 8-byte header so the PC can always find the frame edges.
//
// Packet = header + payload:
//   'Y' 'A'  type  flags  length (uint16 LE)  sequence (uint16 LE)
//   type 'F': a frame, exactly the bytes clocked in while CS was low
//   type 'T': status text, ASCII
//   flags bit 0: the frame filled the buffer, so it may have been cut short
//
// Commands from the PC, one byte each:
//   '2' or '3'  set the SPI mode, saved in flash, then restart. Mode 2 is right
//               for the FTdx101MP: the radio changes DATA2 on the edge mode 3
//               samples on, so mode 3 gets bits flipped.
//   'i'         send a status text packet
//
// Pins, from docs/plan.md "Wiring to the ESP32-S3":
//   GPIO10 CLOCK, GPIO11 DATA2, GPIO13 CS: SPI slave inputs
//   GPIO14 DATA1: plain input, nothing on it so far
//   GPIO38, GPIO39: never touched. The heartbeat is the link between them.
//   GPIO48: the RGB LED

#include <Arduino.h>
#include <Preferences.h>
#include <driver/spi_slave.h>
#include <esp_heap_caps.h>

static constexpr int PIN_CLK = 10;
static constexpr int PIN_DATA2 = 11;   // the radio's data, into the slave's MOSI
static constexpr int PIN_CS = 13;
static constexpr int PIN_DATA1 = 14;
static constexpr int PIN_LED = 48;

// The FT-710's frame is 4096 bytes. Twice that, so a longer frame shows up as
// longer rather than being cut, and a multiple of 4 for the DMA.
static constexpr size_t BUF_BYTES = 8192;
static constexpr int QUEUED = 4;       // transfers ready before CS falls
static constexpr spi_host_device_t HOST = SPI2_HOST;

static WORD_ALIGNED_ATTR uint8_t* bufs[QUEUED];
static spi_slave_transaction_t trans[QUEUED];
static Preferences prefs;
static uint8_t spiMode = 2;           // mode 2 proven on 2026-11-11; mode 3 flips bits
static uint16_t seq = 0;

static uint32_t frames = 0, bytesIn = 0, cutShort = 0, dropped = 0, lastFrameMs = 0;

// USB send buffer: room for several frames, so a slow moment on the PC doesn't
// lose one. Set before Serial.begin().
static constexpr size_t USB_TX_BYTES = 32768;

// A packet goes out whole or not at all. Writing part of one (what happens when
// the buffer is full and the timeout is 0) joins pieces of different frames
// together on the PC. A dropped packet still takes a sequence number, so the PC
// sees the gap.
static void sendPacket(char type, uint8_t flags, const uint8_t* data, uint16_t len) {
    uint8_t h[8] = {'Y', 'A', (uint8_t)type, flags,
                    (uint8_t)(len & 0xFF), (uint8_t)(len >> 8),
                    (uint8_t)(seq & 0xFF), (uint8_t)(seq >> 8)};
    seq++;
    if (Serial.availableForWrite() < (int)(sizeof h + len)) { dropped++; return; }
    Serial.write(h, sizeof h);
    if (len) Serial.write(data, len);
}

static void sendText(const char* text) {
    sendPacket('T', 0, (const uint8_t*)text, strlen(text));
    Serial0.println(text);
}

static void status() {
    char s[160];
    snprintf(s, sizeof s,
             "ACC pipe: SPI mode %u, frames %lu, bytes %lu, cut short %lu, not sent %lu, DATA1 %d, up %lu s",
             spiMode, (unsigned long)frames, (unsigned long)bytesIn, (unsigned long)cutShort,
             (unsigned long)dropped,
             digitalRead(PIN_DATA1), (unsigned long)(millis() / 1000));
    sendText(s);
}

static void led(uint8_t r, uint8_t g, uint8_t b) { neopixelWrite(PIN_LED, r, g, b); }

static void queue(int i) {
    memset(&trans[i], 0, sizeof trans[i]);
    trans[i].length = BUF_BYTES * 8;
    trans[i].rx_buffer = bufs[i];
    trans[i].tx_buffer = nullptr;      // the slave never drives a data line
    trans[i].user = (void*)(intptr_t)i;
    ESP_ERROR_CHECK(spi_slave_queue_trans(HOST, &trans[i], portMAX_DELAY));
}

void setup() {
    Serial.setTxBufferSize(USB_TX_BYTES);
    Serial.setTxTimeoutMs(0);          // no PC reading: drop, don't stall
    Serial.begin();
    Serial0.begin(115200);
    led(0, 0, 8);

    prefs.begin("accpipe", false);
    spiMode = prefs.getUChar("mode", 2);
    if (spiMode != 2 && spiMode != 3) spiMode = 2;

    pinMode(PIN_DATA1, INPUT);

    spi_bus_config_t bus = {};
    bus.mosi_io_num = PIN_DATA2;
    bus.miso_io_num = -1;
    bus.sclk_io_num = PIN_CLK;
    bus.quadwp_io_num = -1;
    bus.quadhd_io_num = -1;
    bus.max_transfer_sz = BUF_BYTES;

    spi_slave_interface_config_t slave = {};
    slave.spics_io_num = PIN_CS;
    slave.mode = spiMode;              // clock idles high: 2 or 3
    slave.queue_size = QUEUED;

    ESP_ERROR_CHECK(spi_slave_initialize(HOST, &bus, &slave, SPI_DMA_CH_AUTO));

    for (int i = 0; i < QUEUED; i++) {
        bufs[i] = (uint8_t*)heap_caps_malloc(BUF_BYTES, MALLOC_CAP_DMA);
        if (!bufs[i]) { sendText("ACC pipe: out of DMA memory"); led(16, 0, 0); while (true) delay(1000); }
        queue(i);
    }
    status();
}

static void commands() {
    while (Serial.available()) {
        int c = Serial.read();
        if (c == '2' || c == '3') {
            prefs.putUChar("mode", (uint8_t)(c - '0'));
            char s[48];
            snprintf(s, sizeof s, "ACC pipe: SPI mode %c saved, restarting", c);
            sendText(s);
            Serial.flush();
            delay(200);
            ESP.restart();
        } else if (c == 'i') {
            status();
        }
    }
}

void loop() {
    spi_slave_transaction_t* done = nullptr;
    if (spi_slave_get_trans_result(HOST, &done, pdMS_TO_TICKS(50)) == ESP_OK) {
        int i = (int)(intptr_t)done->user;
        size_t n = done->trans_len / 8;
        uint8_t flags = n >= BUF_BYTES ? 1 : 0;
        if (flags) cutShort++;
        if (n) {
            sendPacket('F', flags, bufs[i], (uint16_t)n);
            frames++;
            bytesIn += n;
            lastFrameMs = millis();
        }
        queue(i);
        if (frames % 25 == 0) led(0, frames % 50 ? 4 : 12, 0);   // green, pulsing once a second at 25 frames/s
    }
    if (millis() - lastFrameMs > 1000) led(8, 4, 0);             // amber: no frames for a second

    static uint32_t lastStatus = 0;
    if (millis() - lastStatus > 5000) { lastStatus = millis(); status(); }
    commands();
}
