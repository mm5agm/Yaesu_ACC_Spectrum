// Yaesu ACC spectrum firmware, version 2: a dumb pipe, plus edge counts for
// checking the wiring.
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
// Status text, every 5 s and on 'i', always starts "ACC pipe:" then the
// firmware version. "edges/s" counts rising edges in the last second on each
// input, from the ESP32's pulse counter (PCNT), which listens alongside the SPI
// slave without disturbing it. With the radio streaming, expect CS 25, CLOCK
// about 819200 (25 x 4096 x 8) and DATA2 in the thousands. A line reading 0
// while the others count points at that wire.
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
#include <driver/pcnt.h>
#include <driver/gpio.h>
#include <esp_timer.h>

static constexpr const char* FW_VERSION = "2.0.1";

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
// Edge counting. Each input gets a PCNT unit counting rising edges. The
// hardware counter is 16 bits and goes back to 0 at EDGE_LIMIT, so a timer
// reads every counter each 10 ms and adds what it has gained since the last
// read, allowing for the wrap. In 10 ms the 2 MHz clock gives about 8200
// edges, well inside EDGE_LIMIT. (Version 2.0.0 counted the wraps with the
// PCNT interrupt, which never fired, so CLOCK read short.)
struct EdgeInput { const char* name; int pin; pcnt_unit_t unit; };
static EdgeInput edgeInputs[] = {
    {"CS", PIN_CS, PCNT_UNIT_0},
    {"CLOCK", PIN_CLK, PCNT_UNIT_1},
    {"DATA2", PIN_DATA2, PCNT_UNIT_2},
    {"DATA1", PIN_DATA1, PCNT_UNIT_3},
};
static constexpr int EDGE_INPUTS = sizeof edgeInputs / sizeof edgeInputs[0];
static constexpr int16_t EDGE_LIMIT = 30000;
static volatile uint32_t edgeTotals[EDGE_INPUTS];
static int16_t edgeLastCount[EDGE_INPUTS];
static uint32_t edgeLastTotal[EDGE_INPUTS], edgesPerSec[EDGE_INPUTS];

static void edgeSample(void*) {          // every 10 ms, from esp_timer's task
    for (int i = 0; i < EDGE_INPUTS; i++) {
        int16_t c = 0;
        pcnt_get_counter_value(edgeInputs[i].unit, &c);
        int32_t gained = (int32_t)c - edgeLastCount[i];
        if (gained < 0) gained += EDGE_LIMIT;
        edgeLastCount[i] = c;
        edgeTotals[i] += (uint32_t)gained;
    }
}

static void edgeSetup() {
    for (int i = 0; i < EDGE_INPUTS; i++) {
        pcnt_config_t c = {};
        c.pulse_gpio_num = edgeInputs[i].pin;
        c.ctrl_gpio_num = PCNT_PIN_NOT_USED;
        c.channel = PCNT_CHANNEL_0;
        c.unit = edgeInputs[i].unit;
        c.pos_mode = PCNT_COUNT_INC;   // rising edges
        c.neg_mode = PCNT_COUNT_DIS;
        c.lctrl_mode = PCNT_MODE_KEEP;
        c.hctrl_mode = PCNT_MODE_KEEP;
        c.counter_h_lim = EDGE_LIMIT;
        c.counter_l_lim = -1;
        pcnt_unit_config(&c);
        // The driver turns the pull-up on. These lines must stay plain inputs,
        // the same as before, so turn it off again.
        gpio_set_pull_mode((gpio_num_t)edgeInputs[i].pin, GPIO_FLOATING);
        // Ignore pulses shorter than 100 ns (8 cycles at 80 MHz), so ringing
        // on an edge counts once. A clock half-cycle is 250 ns.
        pcnt_set_filter_value(edgeInputs[i].unit, 8);
        pcnt_filter_enable(edgeInputs[i].unit);
        pcnt_counter_clear(edgeInputs[i].unit);
        pcnt_counter_resume(edgeInputs[i].unit);
    }
    esp_timer_create_args_t t = {};
    t.callback = edgeSample;
    t.name = "edges";
    esp_timer_handle_t h;
    ESP_ERROR_CHECK(esp_timer_create(&t, &h));
    ESP_ERROR_CHECK(esp_timer_start_periodic(h, 10000));
}

static void edgeTick() {                 // once a second
    for (int i = 0; i < EDGE_INPUTS; i++) {
        uint32_t t = edgeTotals[i];
        edgesPerSec[i] = t - edgeLastTotal[i];
        edgeLastTotal[i] = t;
    }
}

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
    char s[240];
    int n = snprintf(s, sizeof s,
             "ACC pipe: version %s, SPI mode %u, frames %lu, bytes %lu, cut short %lu, not sent %lu, DATA1 %d, up %lu s, edges/s",
             FW_VERSION, spiMode, (unsigned long)frames, (unsigned long)bytesIn,
             (unsigned long)cutShort, (unsigned long)dropped,
             digitalRead(PIN_DATA1), (unsigned long)(millis() / 1000));
    for (int i = 0; i < EDGE_INPUTS && n > 0 && n < (int)sizeof s; i++)
        n += snprintf(s + n, sizeof s - n, " %s %lu", edgeInputs[i].name,
                      (unsigned long)edgesPerSec[i]);
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

    edgeSetup();

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

    static uint32_t lastEdgeTick = 0;
    if (millis() - lastEdgeTick >= 1000) { lastEdgeTick += 1000; edgeTick(); }

    static uint32_t lastStatus = 0;
    if (millis() - lastStatus > 5000) { lastStatus = millis(); status(); }
    commands();
}
