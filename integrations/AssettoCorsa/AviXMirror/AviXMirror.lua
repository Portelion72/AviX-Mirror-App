-- AviX Mirror : exporte l'état de toutes les voitures pour le rétroviseur VoCore.
-- La mémoire partagée officielle d'Assetto Corsa ne contient que la voiture du joueur ;
-- cette app écrit toutes les voitures dans « AviXMirror.AC.v1 », lue par AviXMirror.exe.
-- Elle rend aussi, sur demande, une vraie caméra arrière (ac.GeometryShot) dans une texture
-- partagée avec AviXMirror.exe : l'image n'est jamais affichée à l'écran du jeu.
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

  int camEnabled;
  int camWidth;
  int camHeight;
  int camFps;
  float camFov;
  float camBack;
  float camUp;

  int64_t camHandle;
  int camFrame;
  int camFormat;
  int camActualWidth;
  int camActualHeight;
  int camStatus;
]])

local FLAG_PITLANE, FLAG_HEADLIGHTS, FLAG_CONNECTED = 1, 2, 4
local VERSION = 2
local CAM_FORMAT_RGBA8 = 2

-- ---------- Caméra arrière ----------

-- Champs cam* : camEnabled..camUp sont écrits par AviXMirror.exe (réglages),
-- camHandle..camStatus par cette app (texture partagée).
local shot, shotW, shotH
local camClock, lastShot, retryAt = 0, -1, 0
local lastHeartbeat, heartbeatAt = 0, 0
local lastError = nil

local function disposeShot()
  if shot then shot:dispose() end
  shot = nil
  mem.camHandle = 0
  mem.camActualWidth, mem.camActualHeight = 0, 0
end

local function updateCamera(dt)
  camClock = camClock + dt
  -- camEnabled est un compteur incrémenté par AviXMirror.exe : s'il ne bouge plus depuis 3 s,
  -- l'appli est fermée et on arrête la caméra pour ne pas consommer de GPU pour rien.
  if mem.camEnabled ~= lastHeartbeat then
    lastHeartbeat, heartbeatAt = mem.camEnabled, camClock
  end
  if mem.camEnabled == 0 or camClock - heartbeatAt > 3 then
    if shot then disposeShot() end
    mem.camStatus = 0
    return
  end

  if camClock < retryAt then return end

  -- On attend d'être vraiment en piste avant de créer la caméra (pas au chargement, ni dans les menus).
  local sim = ac.getSim()
  if camClock < 8 or sim.isInMainMenu or sim.isPaused or sim.isReplayActive then return end

  local w = math.clamp(mem.camWidth > 0 and mem.camWidth or 1280, 64, 2048)
  local h = math.clamp(mem.camHeight > 0 and mem.camHeight or 400, 32, 2048)
  if not shot or w ~= shotW or h ~= shotH then
    disposeShot()
    -- Réglage le plus simple et le plus sûr : format de texture par défaut, sans post-traitement,
    -- avec les shaders utilisés par les rétroviseurs du jeu.
    shot = ac.GeometryShot(ac.findNodes('sceneRoot:yes'), vec2(w, h), 1, false,
      render.AntialiasingMode.None, render.TextureFormat.R8G8B8A8.UNorm, render.TextureFlags.Shared)
    shot:setShadersType(render.ShadersType.SimplifiedWithLights)
    shot:setOriginalLighting(true)
    shot:setSky(true)
    shot:setClippingPlanes(0.25, 2000)
    shotW, shotH = w, h
    mem.camHandle = shot:sharedHandle(true)
    mem.camFormat = CAM_FORMAT_RGBA8
    mem.camActualWidth, mem.camActualHeight = w, h
  end

  local fps = mem.camFps > 0 and mem.camFps or 30
  if camClock - lastShot < 1 / fps then return end
  lastShot = camClock

  -- Caméra placée derrière et au-dessus de la voiture, regardant vers l'arrière :
  -- la voiture du joueur n'est pas dans le champ.
  local car = ac.getCar(0)
  if not car then return end
  local back = mem.camBack > 0 and mem.camBack or 2.4
  local up = mem.camUp ~= 0 and mem.camUp or 1.0
  local fov = mem.camFov > 1 and mem.camFov or 20
  local pos = car.position - car.look * back + car.up * up
  shot:update(pos, car.look * -1, car.up, fov)
  mem.camFrame = mem.camFrame + 1
  mem.camStatus = 1
end

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

  local ok, err = pcall(updateCamera, dt)
  if not ok then
    mem.camStatus = -1
    retryAt = camClock + 5 -- évite de recréer la caméra à chaque image en cas d'erreur
    lastError = err
    ac.debug('AviX Mirror caméra', err)
  end

  mem.version = VERSION
  mem.carsCount = count
  mem.playerIndex = 0 -- la voiture du joueur est toujours la n° 0 dans AC
  mem.trackLength = sim.trackLengthM
  mem.packetId = mem.packetId + 1
  exported = count
end

function windowMain(dt)
  ui.text('AviX Mirror actif')
  ui.text(string.format('%d voiture(s) exportée(s)', exported))
  if mem.camStatus == 1 then
    ui.text(string.format('Caméra arrière : %dx%d', mem.camActualWidth, mem.camActualHeight))
  elseif mem.camStatus == -1 then
    ui.textWrapped('Caméra arrière, erreur : ' .. tostring(lastError))
  end
end
