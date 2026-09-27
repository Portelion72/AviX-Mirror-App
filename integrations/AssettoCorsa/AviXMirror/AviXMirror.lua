-- AviX Mirror : exporte l'état de toutes les voitures pour le rétroviseur VoCore.
-- La mémoire partagée officielle d'Assetto Corsa ne contient que la voiture du joueur ;
-- cette app écrit toutes les voitures dans « AviXMirror.AC.v1 », lue par AviXMirror.exe.
-- La disposition doit rester identique à src/AviXMirror/Radar/AcTelemetry.cs.

local MAX_CARS = 64

local mem = ac.writeMemoryMappedFile('AviXMirror.AC.v1', [[
  int version;
  int packetId;
  int carsCount;
  int playerIndex;
  float trackLength;
  char track[64];
  float posX[64]; float posY[64]; float posZ[64];
  float lookX[64]; float lookY[64]; float lookZ[64];
  float upX[64]; float upY[64]; float upZ[64];
  float velX[64]; float velY[64]; float velZ[64];
  float spline[64];
  int racePosition[64];
  int flags[64];
  char model[64][48];
  char driver[64][32];
]])

local FLAG_PITLANE, FLAG_HEADLIGHTS, FLAG_CONNECTED = 1, 2, 4

local function writeString(dst, size, value)
  value = tostring(value or '')
  if #value > size - 1 then value = value:sub(1, size - 1) end
  ffi.copy(dst, value)
end

local namesTimer = 0
local exported = 0

local function writeNames(count)
  writeString(mem.track, 64, ac.getTrackID() .. '/' .. (ac.getTrackLayout() or ''))
  for i = 0, count - 1 do
    writeString(mem.model[i], 48, ac.getCarID(i))
    writeString(mem.driver[i], 32, ac.getDriverName(i))
  end
end

function script.update(dt)
  local sim = ac.getSim()
  local count = math.min(sim.carsCount, MAX_CARS)

  namesTimer = namesTimer - dt
  if namesTimer <= 0 then
    writeNames(count)
    namesTimer = 2
  end

  for i = 0, count - 1 do
    local car = ac.getCar(i)
    if car then
      mem.posX[i], mem.posY[i], mem.posZ[i] = car.position.x, car.position.y, car.position.z
      mem.lookX[i], mem.lookY[i], mem.lookZ[i] = car.look.x, car.look.y, car.look.z
      mem.upX[i], mem.upY[i], mem.upZ[i] = car.up.x, car.up.y, car.up.z
      mem.velX[i], mem.velY[i], mem.velZ[i] = car.velocity.x, car.velocity.y, car.velocity.z
      mem.spline[i] = car.splinePosition
      mem.racePosition[i] = car.racePosition
      local flags = 0
      if car.isInPitlane then flags = flags + FLAG_PITLANE end
      if car.headlightsActive then flags = flags + FLAG_HEADLIGHTS end
      -- Hors ligne, toutes les voitures (IA comprises) sont présentes.
      if car.isConnected or not sim.isOnlineRace then flags = flags + FLAG_CONNECTED end
      mem.flags[i] = flags
    else
      mem.flags[i] = 0
    end
  end

  mem.version = 1
  mem.carsCount = count
  mem.playerIndex = 0 -- la voiture du joueur est toujours la n° 0 dans AC
  mem.trackLength = sim.trackLengthM
  mem.packetId = mem.packetId + 1
  exported = count
end

function windowMain(dt)
  ui.text('AviX Mirror actif')
  ui.text(string.format('%d voiture(s) exportée(s)', exported))
end
