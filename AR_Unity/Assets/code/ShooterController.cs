using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

namespace ARDashboard
{
    public class ShooterController : MonoBehaviour
    {
        // ── Auto-inyección ────────────────────────────────────────────────────
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Subscribe() => SceneManager.sceneLoaded += OnSceneLoaded;

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "shooter") return;
            var go = new GameObject("[ShooterController]");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<ShooterController>();
        }

        // ── Colores mira ──────────────────────────────────────────────────────
        // C_NORMAL es el verde con el que se CONSTRUYE el HUD. El color que se
        // ve es el del ajuste "HUD DE VUELO" del menú de pausa (verde, negro o
        // blanco, FlightHud.ColorMode): ver RegisterHudTinted / ApplyHudColor.
        static readonly Color C_NORMAL = new Color(0.05f, 1.00f, 0.08f, 1.00f);
        static readonly Color C_HIT    = new Color(1.00f, 0.20f, 0.20f, 1.00f);
        static readonly Color C_WARN   = new Color(1.00f, 0.80f, 0.00f, 1.00f);  // ámbar aviso

        Image[]          crosshairBars;
        Image            centerDot;
        TextMeshProUGUI  hitLabel;
        TextMeshProUGUI  killCounterTmp;
        TextMeshProUGUI[] killCounterLayers;
        AudioSource      audioSrc;
        AudioClip        laserClip;
        Camera           cam;
        bool             shooting;
        public int       killCount;
        public int       shotsFired;
        int              playerHealth  = 100;
        Image            healthBarFill;
        TextMeshProUGUI  healthPctTmp;
        Image            hitOverlayImg;

        // Elementos del HUD que llevan el color del HUD (los que se construyen
        // en C_NORMAL) y el color que tienen aplicado ahora mismo.
        readonly System.Collections.Generic.List<Graphic> hudTinted = new System.Collections.Generic.List<Graphic>();
        FlightHud.HudColor appliedHudColor;

        // Targeting HUD
        RectTransform     crosshairContainer;
        RectTransform     miraContainer;
        CanvasGroup       miraGlow;
        TextMeshProUGUI   miraAcqText;
        Image[]           miraBars;
        bool              hitFlashing;
        bool              hadTarget;
        AITarget          selectedTarget;
        int               currentWeapon;
        TextMeshProUGUI   weaponLabel;
        GameObject        weaponRingRoot;
        float             missileCooldown;
        TextMeshProUGUI   cooldownTmp;
        float             miraScale;
        Image             dangerPanel;
        TextMeshProUGUI   dangerTmp;
        TextMeshProUGUI   threatCountTmp;
        TextMeshProUGUI   lockTmp;
        float             dangerPulseT;
        // MAX_IND == MAX_COUNT del TargetSpawner: nunca habrá más drones que slots
        const int         MAX_IND = 30;
        RectTransform[]   indContainers = new RectTransform[MAX_IND];
        TextMeshProUGUI[] indDistTmps   = new TextMeshProUGUI[MAX_IND];
        TextMeshProUGUI[] indAcqTmps    = new TextMeshProUGUI[MAX_IND];
        RectTransform[]   indLockIcons  = new RectTransform[MAX_IND];

        // A qué AITarget está asignado cada slot de designador — asignación
        // ESTABLE mientras el dron viva (ver AssignIndSlots): FindObjectsByType
        // con FindObjectsSortMode.None no garantiza el mismo orden entre frames,
        // así que indexar targets[i] directamente hacía que un slot "saltara" de
        // un dron a otro de un frame a otro (el rombo parecía desplazarse, y un
        // dron podía quedarse sin designador un instante).
        AITarget[] indOwner = new AITarget[MAX_IND];
        TextMeshProUGUI   headingTmp;
        TextMeshProUGUI   speedTmp;
        TextMeshProUGUI   altTmp;
        Vector3           lastCamPos;
        float             camSpeed;
        RectTransform     fpvCornerTL, fpvCornerTR, fpvCornerBL, fpvCornerBR;
        TextMeshProUGUI   fpvLockText, fpvDistText;
        RectTransform     fpvScaleL, fpvScaleR;
        TextMeshProUGUI   fpvAltText, fpvVelText;
        static Sprite     _tagShapeSpriteLeft, _tagShapeSpriteRight;
        float             fpvTime;
        float             camSpeedSmoothed;
        const int         COMPASS_TICKS = 13;
        const float       COMPASS_SCALE = 1.20f;
        RectTransform[]   compassTickRts  = new RectTransform[13];
        TextMeshProUGUI[] compassTickTmps = new TextMeshProUGUI[13];
        RectTransform     pitchLadderRoot;
        const float       PITCH_PX_DEG = 14f;

        // ── Game-over overlay ─────────────────────────────────────────────────
        Canvas          gameOverCanvas;
        bool            gameOverActive;
        public bool     GameOverActive => gameOverActive;
        Canvas          shooterHudCanvas;

        // ── Inicialización ────────────────────────────────────────────────────
        void Start()
        {
            cam       = Camera.main;
            laserClip = GenerateLaserSound();
            _ = Missile.PreloadAsync();

            audioSrc = gameObject.AddComponent<AudioSource>();
            audioSrc.playOnAwake  = false;
            audioSrc.spatialBlend = 0f;   // sonido 2D (interfaz)
            audioSrc.volume       = 0.28f;

            BuildHUD();
        }

        // ── Input ─────────────────────────────────────────────────────────────
        void Update()
        {
            // Mismo ajuste "HUD DE VUELO: ON/OFF" del menú de pausa que ya usa
            // FlightHud.cs en el resto de juegos — antes de los "return" de pausa
            // para que el cambio se note al momento aunque el juego esté parado.
            bool hudEnabled = FlightHud.Enabled;
            if (shooterHudCanvas != null && shooterHudCanvas.enabled != hudEnabled)
                shooterHudCanvas.enabled = hudEnabled;
            // Y lo mismo con su color (verde / negro / blanco).
            if (shooterHudCanvas != null && appliedHudColor != FlightHud.ColorMode)
                ApplyHudColor();

            if (PauseMenuUI.IsPaused) return;
            if (gameOverActive) return;

            bool fire = Input.GetMouseButtonDown(0)
                     || Input.GetKeyDown(KeyCode.JoystickButton0); // Cuadrado PS4
            if (fire && !shooting)
                StartCoroutine(Shoot());

            if (Input.GetMouseButtonDown(1))               // Click derecho — cambiar arma
                CycleWeapon();

            if (Input.GetKeyDown(KeyCode.JoystickButton5)) // R1 - cambiar objetivo
                CycleTarget();
            if (Input.GetKeyDown(KeyCode.JoystickButton4)) // L1 - cambiar arma
                CycleWeapon();

            // Disparo/arma/objetivo remotos desde la webapp, vía MQTT
            // (dron/<id>/cmd/fire|cycle_weapon|cycle_target -> DroneMqttBridge.cs)
            if (DroneMqttBridge.Instance != null)
            {
                if (DroneMqttBridge.Instance.ConsumeFire() && !shooting)
                    StartCoroutine(Shoot());
                if (DroneMqttBridge.Instance.ConsumeCycleWeapon())
                {
                    CycleWeapon();
                    DroneMqttBridge.Instance.PublishWeaponMode(currentWeapon);
                }
                if (DroneMqttBridge.Instance.ConsumeCycleTarget())
                    CycleTarget(+1);
            }

            // Flechas — cambiar objetivo (solo con misil seleccionado)
            if (currentWeapon == 1)
            {
                if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.DownArrow))
                    CycleTarget(+1);
                if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.UpArrow))
                    CycleTarget(-1);
            }

            UpdateTargetingHUD();
        }

        // ── Disparo ───────────────────────────────────────────────────────────
        IEnumerator Shoot()
        {
            // Misil en cooldown → no disparar
            if (currentWeapon == 1 && missileCooldown > 0f) yield break;

            shooting = true;
            audioSrc.PlayOneShot(laserClip);

            if (cam != null)
            {
                Vector3 origin = cam.transform.position + cam.transform.forward * 0.4f;
                if (currentWeapon == 0)
                {
                    Projectile.Create(origin, cam.transform.forward);
                    shotsFired++;
                }
                else if (selectedTarget != null)
                {
                    Missile.Create(origin, cam.transform.forward, selectedTarget);
                    missileCooldown = 30f;
                    shotsFired++;
                }
            }

            yield return new WaitForSeconds(currentWeapon == 0 ? 0.25f : 1.0f);
            shooting = false;
        }

        // ── Feedback de impacto ───────────────────────────────────────────────
        public void NotifyHit()
        {
            killCount++;
            if (killCounterTmp    != null) killCounterTmp.text = $"DERRIBOS  {killCount}";
            if (killCounterLayers != null)
                foreach (var l in killCounterLayers) if (l) l.text = $"DERRIBOS  {killCount}";
            StartCoroutine(ShowHit());
        }

        IEnumerator ShowHit()
        {
            hitFlashing = true;
            SetCrosshairColor(C_HIT);
            if (hitLabel) hitLabel.gameObject.SetActive(true);
            yield return new WaitForSeconds(0.35f);
            SetCrosshairColor(HudTint(1f));
            if (hitLabel) hitLabel.gameObject.SetActive(false);
            hitFlashing = false;
        }

        void SetCrosshairColor(Color c)
        {
            foreach (var bar in crosshairBars) if (bar) bar.color = c;
            if (centerDot) centerDot.color = c;
        }

        // ── Color del HUD (ajuste "HUD DE VUELO" del menú de pausa) ──────────
        static Color HudTint(float alpha)
        {
            var c = FlightHud.CurrentColor;
            return new Color(c.r, c.g, c.b, alpha);
        }

        // Se llama al terminar de construir el HUD, todavía en C_NORMAL: ese
        // verde exacto solo lo usan los elementos "del color del HUD", así que
        // se distinguen sin confundirlos con textos blancos por defecto, el
        // rojo/ámbar de los designadores, fondos, etc.
        void RegisterHudTinted(Transform root)
        {
            hudTinted.Clear();
            foreach (var g in root.GetComponentsInChildren<Graphic>(true))
            {
                var c = g.color;
                if (Mathf.Approximately(c.r, C_NORMAL.r) && Mathf.Approximately(c.g, C_NORMAL.g)
                    && Mathf.Approximately(c.b, C_NORMAL.b))
                    hudTinted.Add(g);
            }
        }

        // Repinta en vivo, sin reconstruir el HUD (no se pierde vida, derribos,
        // designadores...). Mantiene la transparencia de cada elemento; los
        // halos (HudGlowLink) se sincronizan solos con su trazo.
        void ApplyHudColor()
        {
            appliedHudColor = FlightHud.ColorMode;
            foreach (var g in hudTinted)
            {
                if (g == null) continue;
                g.color = HudTint(g.color.a);
                if (g is TMP_Text t) t.outlineColor = Color.Lerp(t.color, Color.white, 0.35f);
            }
        }

        // ── HUD: altitud, velocidad, orientación ─────────────────────────────
        void UpdateTargetingHUD()
        {
            if (cam == null) return;
            fpvTime += Time.deltaTime;

            // ── Cooldown misil ────────────────────────────────────────────────
            if (missileCooldown > 0f)
            {
                missileCooldown -= Time.deltaTime;
                if (missileCooldown < 0f) missileCooldown = 0f;
            }
            if (cooldownTmp != null)
            {
                bool showCd = currentWeapon == 1 && missileCooldown > 0f;
                cooldownTmp.gameObject.SetActive(showCd);
                if (showCd)
                {
                    int secs = Mathf.CeilToInt(missileCooldown);
                    // Parpadeo rápido en los últimos 5 segundos
                    float alpha = (missileCooldown <= 5f && Mathf.Sin(fpvTime * 12f) < 0f)
                                  ? 0.40f : 0.95f;
                    cooldownTmp.color = HudTint(alpha);
                    cooldownTmp.text  = $"{secs}s";
                }
            }

            camSpeed         = Vector3.Distance(cam.transform.position, lastCamPos) / Time.deltaTime;
            lastCamPos       = cam.transform.position;
            camSpeedSmoothed = Mathf.Lerp(camSpeedSmoothed, camSpeed, Time.deltaTime * 1.5f);

            // ── Pitch ladder: desplaça verticalment amb pitch i gira amb roll ────
            if (pitchLadderRoot != null)
            {
                float rawPitch = cam.transform.eulerAngles.x;
                float pitchDeg = rawPitch > 180f ? 360f - rawPitch : -rawPitch;
                pitchLadderRoot.anchoredPosition = new Vector2(0f, -pitchDeg * PITCH_PX_DEG);

                float rawRoll  = cam.transform.eulerAngles.z;
                float rollDeg  = rawRoll > 180f ? rawRoll - 360f : rawRoll;
                // Rota solo el contenido (no la máscara) para que el clip
                // sea simétrico y no dependa de la dirección del roll.
                pitchLadderRoot.localEulerAngles = new Vector3(0f, 0f, -rollDeg);
            }

            // ── Escalas: la cinta se desplaza segun el valor real ──────────────
            const float SCALE_YSTEP = 56f;
            float altVal = cam.transform.position.y;
            float velVal = camSpeedSmoothed;
            if (fpvScaleL != null) fpvScaleL.anchoredPosition = new Vector2(0f, -altVal * SCALE_YSTEP);
            if (fpvScaleR != null) fpvScaleR.anchoredPosition = new Vector2(0f, -velVal * SCALE_YSTEP);
            if (fpvAltText != null) fpvAltText.text = $"{altVal:F1}";
            if (fpvVelText != null) fpvVelText.text = $"{velVal:F1}";

            // ── Compass ───────────────────────────────────────────────────────
            float hdg = cam.transform.eulerAngles.y;
            if (headingTmp != null)
            {
                int hdgRounded = Mathf.RoundToInt(hdg) % 360;
                headingTmp.text = $"{hdgRounded}°";
            }
            if (compassTickRts[0] != null)
            {
                const float PX_DEG = 360f/120f, HALF_RNG = 60f;
                float minDeg = hdg - HALF_RNG;
                int first = Mathf.CeilToInt(minDeg / 10f) * 10;
                for (int ti = 0; ti < COMPASS_TICKS; ti++)
                {
                    float divDeg = first + ti * 10f;
                    float x = (divDeg - hdg) * PX_DEG;
                    if (Mathf.Abs(x) > HALF_RNG * PX_DEG + 20f)
                    { compassTickRts[ti].gameObject.SetActive(false); continue; }
                    compassTickRts[ti].gameObject.SetActive(true);
                    compassTickRts[ti].anchoredPosition = new Vector2(x, 0f);
                    int norm = (int)(divDeg % 360 + 360) % 360;
                    if (compassTickTmps[ti] != null)
                        compassTickTmps[ti].text = norm % 30 == 0 ? CompassLabel(norm) : "";
                }
            }


            // ── Targets y lock text ───────────────────────────────────────────
            var targets = FindObjectsByType<AITarget>(FindObjectsSortMode.None);
            float nearest = float.MaxValue;
            bool  inSights = false;
            foreach (var t in targets)
            {
                float d = Vector3.Distance(cam.transform.position, t.transform.position);
                if (d < nearest) nearest = d;
                var dir = (t.transform.position - cam.transform.position).normalized;
                if (Vector3.Angle(cam.transform.forward, dir) <= 6f) inSights = true;
            }

            // ── Designadores dinámicos (sin límite de drones) ────────────────
            const float RW           = 1920f, RH = 1080f;
            const float ACQUIRE_TIME = 2.8f;
            float   nearestOnScreen    = float.MaxValue;
            Vector2 crosshairCanvasPos = Vector2.zero;

            AssignIndSlots(targets);

            for (int i = 0; i < MAX_IND; i++)
            {
                AITarget t = indOwner[i];
                if (t == null) { HideInd(i); continue; }
                Vector3 sp    = cam.WorldToScreenPoint(t.VisualCenter);
                float   dist2 = Vector3.Distance(cam.transform.position, t.transform.position);
                bool onScreen = sp.z > 0f && sp.x > 0 && sp.x < Screen.width
                                          && sp.y > 0 && sp.y < Screen.height;
                Vector2 cp = new Vector2((sp.x / Screen.width  - .5f) * RW,
                                         (sp.y / Screen.height - .5f) * RH);
                if (onScreen)
                {
                    // Acumular timer de adquisición per dron
                    t.acquireTimer += Time.deltaTime;
                    if (t.acquireTimer >= ACQUIRE_TIME)
                        t.isAcquired = true;

                    if (indContainers[i] != null)
                    {
                        // Diamante: ámbar mientras adquiere, rojo cuando locked
                        indContainers[i].gameObject.SetActive(true);
                        indContainers[i].anchoredPosition = cp;
                        Color indCol;
                        if (t.isAcquired)
                            indCol = new Color(1.00f, 0.15f, 0.15f, 0.85f);
                        else {
                            float aBlink = Mathf.Sin(fpvTime * 9f) > 0f ? 0.75f : 0.40f;
                            indCol = new Color(1.00f, 0.75f, 0.00f, aBlink);
                        }
                        foreach (var img in indContainers[i].GetComponentsInChildren<Image>())
                            if (img != null) img.color = indCol;
                    }
                    if (indDistTmps[i] != null)
                    {
                        // Distancia: solo cuando locked
                        indDistTmps[i].gameObject.SetActive(t.isAcquired);
                        indDistTmps[i].text = $"{dist2:F1}m";
                        indDistTmps[i].rectTransform.anchoredPosition = cp + new Vector2(0f, -48f);
                        indDistTmps[i].color = new Color(1.00f, 0.00f, 0.00f, 1.00f);
                    }
                    if (indAcqTmps[i] != null)
                    {
                        indAcqTmps[i].gameObject.SetActive(true);
                        indAcqTmps[i].rectTransform.anchoredPosition = cp + new Vector2(0f, 55f);
                        if (!t.isAcquired)
                        {
                            if (indLockIcons[i] != null) indLockIcons[i].gameObject.SetActive(false);
                            float blink = Mathf.Sin(fpvTime * 9f) > 0f ? 1.00f : 0.70f;
                            indAcqTmps[i].color     = new Color(1.00f, 0.82f, 0.00f, blink);
                            indAcqTmps[i].fontStyle = FontStyles.Bold;
                            indAcqTmps[i].fontSize  = 9f;
                            int dots = (int)(fpvTime * 2.5f) % 4;
                            indAcqTmps[i].text = "ACQUIRING TARGET" + new string('.', dots);
                        }
                        else
                        {
                            float p = (Mathf.Sin(fpvTime * 2f) + 1f) * .5f;
                            float lockedAlpha = Mathf.Lerp(0.85f, 1.00f, p);
                            indAcqTmps[i].color     = new Color(1.00f, 0.00f, 0.00f, lockedAlpha);
                            indAcqTmps[i].fontStyle = FontStyles.Bold;
                            indAcqTmps[i].fontSize  = 13f;
                            indAcqTmps[i].text = "TARGET LOCKED";
                            // Icono de mira a la izquierda del texto
                            if (indLockIcons[i] != null)
                            {
                                indLockIcons[i].gameObject.SetActive(true);
                                indLockIcons[i].anchoredPosition = cp + new Vector2(-88f, 55f);
                                foreach (var img in indLockIcons[i].GetComponentsInChildren<Image>())
                                    if (img) img.color = new Color(1f, 0f, 0f, lockedAlpha);
                            }
                        }
                    }
                    if (dist2 < nearestOnScreen)
                    {
                        nearestOnScreen    = dist2;
                        crosshairCanvasPos = cp;
                    }
                }
                else
                {
                    // Fuera de pantalla: resetear adquisición
                    t.acquireTimer = 0f;
                    t.isAcquired   = false;
                    HideInd(i);
                }
            }
            if (crosshairContainer != null)
                crosshairContainer.anchoredPosition = Vector2.zero;

            // ── Auto-designar el dron más reciente si no hay objetivo ─────────
            if (selectedTarget == null || !selectedTarget.gameObject.activeInHierarchy)
            {
                AITarget newest = null;
                float    newestT = float.MinValue;
                foreach (var t in targets)
                {
                    if (t != null && t.spawnTime > newestT)
                    {
                        newestT = t.spawnTime;
                        newest  = t;
                    }
                }
                selectedTarget = newest;
            }

            // ── miraContainer sigue al objetivo seleccionado con R1 ───────────
            bool hasTarget = nearestOnScreen < float.MaxValue;
            if (selectedTarget != null && !selectedTarget.gameObject.activeInHierarchy)
                selectedTarget = null;
            if (miraContainer != null)
            {
                bool showMira = selectedTarget != null && selectedTarget.isAcquired && currentWeapon == 1;
                miraContainer.gameObject.SetActive(showMira);
                if (showMira)
                {
                    var selSp = cam.WorldToScreenPoint(selectedTarget.VisualCenter);
                    bool selOn = selSp.z > 0 && selSp.x > 0 && selSp.x < Screen.width
                              && selSp.y > 0 && selSp.y < Screen.height;
                    if (selOn)
                    {
                        Vector2 selCp = new Vector2((selSp.x / Screen.width  - .5f) * 1920f,
                                                    (selSp.y / Screen.height - .5f) * 1080f);
                        miraContainer.anchoredPosition = selCp;
                        miraContainer.localScale = Vector3.one;
                        float breath = Mathf.Sin(fpvTime * 1.4f) * 2f;
                        float mcr = 42f + breath;
                        if (fpvCornerTL) fpvCornerTL.anchoredPosition = new Vector2(-mcr,  mcr);
                        if (fpvCornerTR) fpvCornerTR.anchoredPosition = new Vector2( mcr,  mcr);
                        if (fpvCornerBL) fpvCornerBL.anchoredPosition = new Vector2(-mcr, -mcr);
                        if (fpvCornerBR) fpvCornerBR.anchoredPosition = new Vector2( mcr, -mcr);
                    }
                    else miraContainer.gameObject.SetActive(false);
                }
            }

            hadTarget = hasTarget;


            // ── ACQUIRING TARGET en la mira ───────────────────────────────────
            if (miraAcqText != null)
            {
                bool show = hasTarget && (selectedTarget == null || !selectedTarget.isAcquired);
                miraAcqText.gameObject.SetActive(show);
                if (show)
                {
                    float blink = Mathf.Sin(fpvTime * 9f) > 0f ? 1.00f : 0.70f;
                    miraAcqText.color = new Color(1.00f, 0.82f, 0.00f, blink);
                    int dots = (int)(fpvTime * 2.5f) % 4;
                    miraAcqText.text = "ACQUIRING TARGET" + new string('.', dots);
                }
            }

            // ── Efecto uranio/láser: glow pulsante con ruido Perlin ───────────
            if (miraGlow != null)
                miraGlow.alpha = 0.88f + 0.12f * Mathf.PerlinNoise(fpvTime * 10f, 0f);

            if (!hitFlashing && crosshairBars != null)
            {
                float flicker = 0.90f + 0.10f * Mathf.PerlinNoise(fpvTime * 13f, 5f);
                Color glowCol = HudTint(flicker);
                foreach (var bar in crosshairBars) if (bar) bar.color = glowCol;
                if (centerDot) centerDot.color = glowCol;
            }
        }

        // Mantiene indOwner estable: cada AITarget conserva su mismo slot de
        // designador mientras siga vivo (independientemente del orden, variable
        // entre frames, en que FindObjectsByType devuelva la lista), y solo lo
        // libera cuando el dron se destruye.
        void AssignIndSlots(AITarget[] targets)
        {
            for (int i = 0; i < MAX_IND; i++)
                if (indOwner[i] == null) indOwner[i] = null; // normaliza referencias a drones destruidos ("null" de Unity) a null real

            foreach (var t in targets)
            {
                bool alreadyOwned = false;
                for (int i = 0; i < MAX_IND; i++)
                    if (indOwner[i] == t) { alreadyOwned = true; break; }
                if (alreadyOwned) continue;

                for (int i = 0; i < MAX_IND; i++)
                {
                    if (indOwner[i] == null) { indOwner[i] = t; break; }
                }
            }
        }

        void HideInd(int i)
        {
            if (indContainers[i] != null) indContainers[i].gameObject.SetActive(false);
            if (indDistTmps[i]   != null) indDistTmps[i].gameObject.SetActive(false);
            if (indAcqTmps[i]    != null) indAcqTmps[i].gameObject.SetActive(false);
            if (indLockIcons[i]  != null) indLockIcons[i].gameObject.SetActive(false);
        }

        void CycleTarget(int dir = 1)
        {
            if (cam == null) return;
            var all = FindObjectsByType<AITarget>(FindObjectsSortMode.None);
            var visible = new System.Collections.Generic.List<AITarget>();
            foreach (var t in all)
            {
                var sp = cam.WorldToScreenPoint(t.transform.position);
                if (sp.z > 0 && sp.x > 0 && sp.x < Screen.width && sp.y > 0 && sp.y < Screen.height)
                    visible.Add(t);
            }
            if (visible.Count == 0) { selectedTarget = null; return; }
            int idx  = selectedTarget != null ? visible.IndexOf(selectedTarget) : -1;
            int next = ((idx + dir) % visible.Count + visible.Count) % visible.Count;
            selectedTarget = visible[next];
        }

        void CycleWeapon()
        {
            currentWeapon = (currentWeapon + 1) % 2;
            if (weaponLabel != null) weaponLabel.text = currentWeapon == 0 ? "LASER" : "MISSILE";
        }

        void UpdateFPVScale(RectTransform tickRoot, float val, int step, string side)
        {
            if (tickRoot == null) return;
            int half = 7;
            for (int i = -half; i <= half; i++)
            {
                string name = $"ScaleTick_{side}_{i * step}";
                var child = tickRoot.Find(name);
                if (child == null) continue;
                var tmp = child.GetComponent<TextMeshProUGUI>();
                if (tmp != null) tmp.text = (Mathf.RoundToInt(val) + i * step).ToString();
            }
        }


        static string CompassLabel(int deg)
        {
            return deg == 0   ? "N" : deg == 90  ? "E"
                 : deg == 180 ? "S" : deg == 270 ? "W"
                 : $"{deg:000}";
        }
        Vector2 ClampToEdge(Vector2 dir, float halfW, float halfH)
        {
            if (Mathf.Abs(dir.x) < 0.001f)
                return new Vector2(0f, Mathf.Sign(dir.y) * halfH);
            float tx = halfW / Mathf.Abs(dir.x);
            float ty = halfH / Mathf.Abs(dir.y);
            return dir * Mathf.Min(tx, ty);
        }

        public void TakeDamage(int amount)
        {
            if (gameOverActive) return;
            playerHealth = Mathf.Max(0, playerHealth - amount);
            float pct = playerHealth / 100f;

            if (healthBarFill != null)
            {
                var rt = healthBarFill.rectTransform;
                rt.anchorMax = new Vector2(pct, 1f);
                rt.offsetMax = new Vector2(-1f, -1f);
            }
            if (healthPctTmp != null)
                healthPctTmp.text = $"{playerHealth}%";

            if (playerHealth <= 0)
                StartCoroutine(ShowGameOver());
            else
                StartCoroutine(ShowPlayerHit());
        }

        IEnumerator ShowGameOver()
        {
            gameOverActive = true;
            if (shooterHudCanvas != null) shooterHudCanvas.gameObject.SetActive(false);
            if (!ShooterSettings.RespawnEnabled)
                yield return StartCoroutine(PlayDeathExplosion());
            BuildGameOverOverlay();
        }

        IEnumerator PlayDeathExplosion()
        {
            // ── Canvas de explosión (sobre todo lo demás) ─────────────────────
            var exGO = new GameObject("ExplosionCanvas");
            exGO.transform.SetParent(transform);
            var exCanvas = exGO.AddComponent<Canvas>();
            exCanvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            exCanvas.sortingOrder = 700;
            var exScaler = exGO.AddComponent<CanvasScaler>();
            exScaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            exScaler.referenceResolution = new Vector2(1920f, 1080f);
            exGO.AddComponent<GraphicRaycaster>();

            // ── Fondo pantalla completa ───────────────────────────────────────
            var bgGO = new GameObject("BG"); bgGO.transform.SetParent(exGO.transform, false);
            var bgImg = bgGO.AddComponent<Image>(); bgImg.raycastTarget = false;
            var bgRt = bgGO.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
            bgRt.sizeDelta = bgRt.anchoredPosition = Vector2.zero;

            // ── Contenedor de brackets expansivos ────────────────────────────
            var brkGO = new GameObject("Brackets"); brkGO.transform.SetParent(exGO.transform, false);
            var brkRt = brkGO.AddComponent<RectTransform>();
            brkRt.anchorMin = brkRt.anchorMax = brkRt.pivot = new Vector2(0.5f, 0.5f);
            brkRt.anchoredPosition = Vector2.zero; brkRt.sizeDelta = Vector2.zero;

            // Helper: segmento de imagen dentro del contenedor
            void Seg(float px, float py, float sw, float sh)
            {
                var g = new GameObject("S"); g.transform.SetParent(brkRt, false);
                var img = g.AddComponent<Image>(); img.raycastTarget = false;
                var rt = g.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(px, py); rt.sizeDelta = new Vector2(sw, sh);
            }

            const float BL = 55f, BT = 5f, BR = 70f;
            // TL
            Seg(-(BR + BL * .5f),  BR, BL, BT);  Seg(-BR,  BR + BL * .5f, BT, BL);
            // TR
            Seg( (BR + BL * .5f),  BR, BL, BT);  Seg( BR,  BR + BL * .5f, BT, BL);
            // BR
            Seg( (BR + BL * .5f), -BR, BL, BT);  Seg( BR, -(BR + BL * .5f), BT, BL);
            // BL
            Seg(-(BR + BL * .5f), -BR, BL, BT);  Seg(-BR, -(BR + BL * .5f), BT, BL);

            var brkImgs = brkRt.GetComponentsInChildren<Image>();

            // ── Texto DRONE DESTRUIDO ─────────────────────────────────────────
            var txtGO = new GameObject("Txt"); txtGO.transform.SetParent(exGO.transform, false);
            var txt = txtGO.AddComponent<TextMeshProUGUI>();
            txt.text = "DRONE DESTRUIDO"; txt.fontSize = 52f;
            txt.fontStyle = FontStyles.Bold; txt.alignment = TextAlignmentOptions.Center;
            txt.characterSpacing = 20f; txt.raycastTarget = false;
            var txtRt = txtGO.GetComponent<RectTransform>();
            txtRt.anchorMin = txtRt.anchorMax = txtRt.pivot = new Vector2(0.5f, 0.5f);
            txtRt.anchoredPosition = new Vector2(0f, -80f); txtRt.sizeDelta = new Vector2(900f, 80f);

            // ── Fase 1: flash blanco → rojo (0.3 s) ──────────────────────────
            bgImg.color  = new Color(1f, 1f, 1f, 0.95f);
            txt.color    = Color.clear;
            brkGO.transform.localScale = Vector3.one * 0.3f;
            foreach (var img in brkImgs) img.color = Color.clear;

            float t = 0f;
            while (t < 0.3f)
            {
                t += Time.deltaTime;
                bgImg.color = Color.Lerp(new Color(1f, 1f, 1f, 0.95f),
                                         new Color(0.7f, 0.04f, 0.04f, 0.80f),
                                         t / 0.3f);
                yield return null;
            }

            // ── Fase 2: brackets se expanden + texto aparece (0.8 s) ─────────
            t = 0f;
            while (t < 0.8f)
            {
                t += Time.deltaTime;
                float p  = Mathf.Clamp01(t / 0.8f);
                float sp = p * p * (3f - 2f * p);   // smoothstep

                bgImg.color = Color.Lerp(new Color(0.7f, 0.04f, 0.04f, 0.80f),
                                          new Color(0.4f, 0.02f, 0.02f, 0.50f), sp);

                // Brackets escalan 0.3 → 2.2 con suavidad
                brkGO.transform.localScale = Vector3.one * Mathf.Lerp(0.3f, 2.2f, sp);
                float ba = p < 0.3f ? p / 0.3f : 1f;
                foreach (var img in brkImgs) img.color = new Color(1f, 0.12f, 0.12f, ba);

                // Texto aparece en la segunda mitad
                if (t > 0.35f)
                {
                    float ta = Mathf.Clamp01((t - 0.35f) / 0.35f);
                    txt.color = new Color(1f, 0.15f, 0.15f, ta);
                }
                yield return null;
            }

            // ── Fase 3: parpadeo agresivo con texto visible (1.0 s) ───────────
            t = 0f;
            while (t < 1.0f)
            {
                t += Time.deltaTime;
                float blink = Mathf.Sin(t * 16f) > 0f ? 1f : 0.45f;
                foreach (var img in brkImgs) img.color = new Color(1f, 0.12f, 0.12f, blink);
                bgImg.color = new Color(0.4f, 0.02f, 0.02f, 0.50f);
                yield return null;
            }

            // ── Fase 4: fade out (0.5 s) → deja paso al game-over panel ──────
            t = 0f;
            while (t < 0.5f)
            {
                t += Time.deltaTime;
                float a = 1f - t / 0.5f;
                bgImg.color = new Color(0.4f, 0.02f, 0.02f, 0.50f * a);
                txt.color   = new Color(1f, 0.15f, 0.15f, a);
                foreach (var img in brkImgs) img.color = new Color(1f, 0.12f, 0.12f, a);
                yield return null;
            }

            Destroy(exGO);
        }

        void BuildGameOverOverlay()
        {
            // ── Paleta idéntica a PauseMenuUI ─────────────────────────────────
            var CC_CARD   = new Color(0.07f, 0.09f, 0.14f, 1f);
            var CC_ACCENT = new Color(0.15f, 1.00f, 0.70f, 1f);
            var CC_CYAN   = new Color(0.00f, 0.95f, 1.00f, 1f);
            var CC_BORDER = new Color(0.16f, 0.22f, 0.32f, 1f);
            var CC_TEXT   = new Color(0.90f, 0.95f, 1.00f, 1f);
            var CC_SUB    = new Color(0.42f, 0.58f, 0.72f, 1f);
            var CC_DISAB  = new Color(0.18f, 0.22f, 0.30f, 1f);

            // ── Canvas ────────────────────────────────────────────────────────
            var cGO = new GameObject("GameOverCanvas");
            cGO.transform.SetParent(transform);
            gameOverCanvas = cGO.AddComponent<Canvas>();
            gameOverCanvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            gameOverCanvas.sortingOrder = 600;
            var scaler = cGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            cGO.AddComponent<GraphicRaycaster>();

            // ── Helpers locales ───────────────────────────────────────────────
            Image GoImg(Transform par, Color col, bool stretch = false)
            {
                var g = new GameObject("I"); g.transform.SetParent(par, false);
                var im = g.AddComponent<Image>(); im.color = col;
                if (stretch)
                {
                    var r2 = g.GetComponent<RectTransform>();
                    r2.anchorMin = Vector2.zero; r2.anchorMax = Vector2.one;
                    r2.offsetMin = r2.offsetMax = Vector2.zero;
                }
                return im;
            }
            TextMeshProUGUI GoLbl(Transform par, string text, Vector2 pos, float fs, Color col,
                       Vector2 sz, float spacing = 0f)
            {
                var g = new GameObject("L"); g.transform.SetParent(par, false);
                var t = g.AddComponent<TextMeshProUGUI>();
                t.text = text; t.fontSize = fs; t.color = col;
                t.fontStyle = FontStyles.Bold; t.alignment = TextAlignmentOptions.Center;
                t.characterSpacing = spacing; t.raycastTarget = false;
                var r = g.GetComponent<RectTransform>();
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 1f);
                r.pivot = new Vector2(0.5f, 1f);
                r.anchoredPosition = pos; r.sizeDelta = sz;
                return t;
            }
            Button GoBtn(Transform par, string label, Vector2 pos, Vector2 sz,
                         Color fill, Color textCol)
            {
                var g = new GameObject("B"); g.transform.SetParent(par, false);
                var im = g.AddComponent<Image>(); im.color = fill;
                var r = g.GetComponent<RectTransform>();
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 1f);
                r.pivot = new Vector2(0.5f, 1f);
                r.anchoredPosition = pos; r.sizeDelta = sz;
                var tG = new GameObject("T"); tG.transform.SetParent(g.transform, false);
                var t = tG.AddComponent<TextMeshProUGUI>();
                t.text = label; t.fontSize = 12f; t.fontStyle = FontStyles.Bold;
                t.color = textCol; t.alignment = TextAlignmentOptions.Center;
                t.characterSpacing = 14f; t.raycastTarget = false;
                var tRt = tG.GetComponent<RectTransform>();
                tRt.anchorMin = Vector2.zero; tRt.anchorMax = Vector2.one;
                tRt.offsetMin = tRt.offsetMax = Vector2.zero;
                var btn = g.AddComponent<Button>();
                var cb = btn.colors;
                cb.normalColor      = fill;
                cb.highlightedColor = Color.Lerp(fill, Color.white, 0.2f);
                cb.pressedColor     = Color.Lerp(fill, Color.black, 0.2f);
                btn.colors = cb;
                return btn;
            }

            // ── Fondo oscuro + viñeta ─────────────────────────────────────────
            GoImg(cGO.transform, new Color(0.04f, 0.06f, 0.10f, 0.82f), true);
            GoImg(cGO.transform, new Color(0f, 0f, 0f, 0.45f), true);

            // ── Card central ──────────────────────────────────────────────────
            float cardH = ShooterSettings.RespawnEnabled ? 270f : 250f;
            var cardImg = GoImg(cGO.transform, CC_CARD);
            var cardRt = cardImg.GetComponent<RectTransform>();
            cardRt.anchorMin = cardRt.anchorMax = cardRt.pivot = new Vector2(0.5f, 0.5f);
            cardRt.sizeDelta = new Vector2(380f, cardH);
            cardRt.anchoredPosition = Vector2.zero;
            var outline = cardImg.gameObject.AddComponent<Outline>();
            outline.effectColor = CC_BORDER; outline.effectDistance = new Vector2(1f, -1f);

            // Degradado oscuro en la parte inferior
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.Lerp(0.88f, 0f, i / 9f);
                var b = new GameObject("GD"); b.transform.SetParent(cardImg.transform, false);
                b.transform.SetAsFirstSibling();
                var bI = b.AddComponent<Image>(); bI.color = new Color(0f,0f,0f,a); bI.raycastTarget = false;
                var bR = b.GetComponent<RectTransform>();
                bR.anchorMin = new Vector2(0f, i/10f); bR.anchorMax = new Vector2(1f,(i+1)/10f);
                bR.offsetMin = bR.offsetMax = Vector2.zero;
            }

            // Barra decorativa superior (10 % ancho, 4 px)
            var tb = GoImg(cardImg.transform, CC_CYAN); tb.raycastTarget = false;
            var tbRt = tb.GetComponent<RectTransform>();
            tbRt.anchorMin = new Vector2(0f,1f); tbRt.anchorMax = new Vector2(0.10f,1f);
            tbRt.pivot = new Vector2(0f,1f);
            tbRt.offsetMin = tbRt.offsetMax = Vector2.zero;
            tbRt.sizeDelta = new Vector2(0f, 4f);

            // ── Contenido de la card ──────────────────────────────────────────
            GoLbl(cardImg.transform, "SISTEMA AR·V1.0",
                new Vector2(0f,-18f), 7.5f, CC_CYAN, new Vector2(340f,13f), 22f);
            var goTitleLbl = GoLbl(cardImg.transform, "DRON DERRIBADO",
                new Vector2(0f,-50f), 20f, CC_TEXT, new Vector2(340f,38f));
            var orbitronGo = MapCreatorUI.GetOrbitronFontAsset();
            if (orbitronGo != null) goTitleLbl.font = orbitronGo;

            GoLbl(cardImg.transform, $"DERRIBOS: {killCount}",
                new Vector2(0f,-98f), 13f, CC_SUB, new Vector2(340f,20f));

            if (ShooterSettings.RespawnEnabled)
            {
                var btnYes = GoBtn(cardImg.transform, "REINICIAR",
                    new Vector2(0f,-136f), new Vector2(320f,44f),
                    CC_ACCENT, new Color(0.03f,0.05f,0.09f,1f));
                btnYes.onClick.AddListener(RestartGame);

                var btnNo = GoBtn(cardImg.transform, "VOLVER AL MENÚ",
                    new Vector2(0f,-190f), new Vector2(320f,44f),
                    CC_DISAB, CC_TEXT);
                btnNo.onClick.AddListener(ReturnToMenu);
            }
            else
            {
                var btnRestart = GoBtn(cardImg.transform, "↺  REINICIAR",
                    new Vector2(-85f,-180f), new Vector2(150f,44f),
                    CC_ACCENT, new Color(0.03f,0.05f,0.09f,1f));
                btnRestart.onClick.AddListener(RestartGame);

                var btnExit = GoBtn(cardImg.transform, "◄  SALIR",
                    new Vector2(85f,-180f), new Vector2(150f,44f),
                    CC_DISAB, CC_TEXT);
                btnExit.onClick.AddListener(ReturnToMenu);
            }

            // ── Borde luminoso (glow) alrededor del card ──────────────────────
            float gw = 380f, gt = 2f;
            Color gc = new Color(CC_CYAN.r, CC_CYAN.g, CC_CYAN.b, 0.5f);
            Vector2[] gPos = { new Vector2(0f,  cardH*.5f), new Vector2(0f, -cardH*.5f),
                               new Vector2(-gw*.5f,0f),     new Vector2( gw*.5f,0f) };
            Vector2[] gSz  = { new Vector2(gw,gt), new Vector2(gw,gt),
                               new Vector2(gt,cardH), new Vector2(gt,cardH) };
            for (int gi = 0; gi < 4; gi++)
            {
                var go = new GameObject("GB"); go.transform.SetParent(cGO.transform, false);
                var gi2 = go.AddComponent<Image>(); gi2.color = gc; gi2.raycastTarget = false;
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f,0.5f);
                rt.anchoredPosition = gPos[gi]; rt.sizeDelta = gSz[gi];
            }
        }

        void ReturnToMenu()
        {
            SceneManager.LoadScene("menu");
        }

        void RestartGame()
        {
            ShooterSettings.GameStarted = true;   // omitir el config menu al recargar
            SceneManager.LoadScene("shooter");
        }

        // ── Helpers para el overlay de game-over ──────────────────────────────
        void MakePanelBorder(Transform p, Vector2 sz, Color c)
        {
            float hw = sz.x * 0.5f, hh = sz.y * 0.5f;
            MakeGoBar(p, new Vector2(0f,  hh), new Vector2(sz.x, 2f), c, 0.7f);
            MakeGoBar(p, new Vector2(0f, -hh), new Vector2(sz.x, 2f), c, 0.7f);
            MakeGoBar(p, new Vector2(-hw, 0f), new Vector2(2f, sz.y), c, 0.7f);
            MakeGoBar(p, new Vector2( hw, 0f), new Vector2(2f, sz.y), c, 0.7f);
        }

        void MakeGoBar(Transform p, Vector2 pos, Vector2 sz, Color c, float a)
        {
            var go = new GameObject("R"); go.transform.SetParent(p, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(c.r, c.g, c.b, a); img.raycastTarget = false;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = sz;
        }

        TextMeshProUGUI MakeGoLabel(Transform p, string text, Vector2 pos, Vector2 sz,
            float fs, Color c, TextAlignmentOptions align)
        {
            var go = new GameObject("L"); go.transform.SetParent(p, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = fs; tmp.fontStyle = FontStyles.Bold;
            tmp.color = c; tmp.alignment = align; tmp.raycastTarget = false;
            tmp.characterSpacing = 8f;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = sz;
            return tmp;
        }

        GameObject MakeGoButton(Transform p, string label, Vector2 pos, Vector2 sz,
            Color bgColor, Color textColor)
        {
            var go = new GameObject("Btn"); go.transform.SetParent(p, false);
            var img = go.AddComponent<Image>(); img.color = bgColor;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = sz;

            var btn = go.AddComponent<Button>();
            var cols = btn.colors;
            cols.highlightedColor = new Color(0.8f, 1f, 0.8f, 1f);
            cols.pressedColor     = new Color(0.6f, 1f, 0.6f, 1f);
            btn.colors = cols;

            MakeGoLabel(go.transform, label, Vector2.zero, sz - new Vector2(4f, 4f),
                14f, textColor, TextAlignmentOptions.Center);

            // Borde del botón
            MakePanelBorder(go.transform, sz, textColor);
            return go;
        }

        IEnumerator ShowPlayerHit()
        {
            if (hitOverlayImg) hitOverlayImg.gameObject.SetActive(true);
            float t = 0f;
            while (t < 0.5f)
            {
                t += Time.deltaTime;
                if (hitOverlayImg)
                    hitOverlayImg.color = new Color(1f, 0f, 0f, 0.35f * (1f - t / 0.5f));
                yield return null;
            }
            if (hitOverlayImg) hitOverlayImg.gameObject.SetActive(false);
        }

        // ── Sonido bláster Star Wars procedural ──────────────────────────────
        // Inspirado en la técnica de Ben Burtt: golpe en cables tensores de
        // torre de radio → descenso rápido de tono con resonancia de muelle.
        static AudioClip GenerateLaserSound()
        {
            const int   RATE     = 44100;
            const float DURATION = 0.28f;
            int         samples  = (int)(RATE * DURATION);
            float[]     data     = new float[samples];

            float mainPhase = 0f;
            var   rng       = new System.Random(7);

            for (int i = 0; i < samples; i++)
            {
                float t        = (float)i / RATE;
                float progress = t / DURATION;

                // Descenso exponencial de tono (900 → 140 Hz) — más corto y suave
                float baseFreq = 900f * Mathf.Exp(-progress * 11f) + 140f;
                float wobble   = Mathf.Sin(2f * Mathf.PI * 6f * t)
                               * 35f * Mathf.Exp(-progress * 18f);
                float freq     = baseFreq + wobble;
                mainPhase += 2f * Mathf.PI * freq / RATE;

                // Armónicos impares para timbre metálico (cuerda/cable tensado)
                float wave = Mathf.Sin(mainPhase)
                           + 0.45f * Mathf.Sin(mainPhase * 2f)
                           + 0.22f * Mathf.Sin(mainPhase * 3f)
                           + 0.10f * Mathf.Sin(mainPhase * 4f)
                           + 0.05f * Mathf.Sin(mainPhase * 5f);

                // Transiente de impacto al inicio (chasquido metálico)
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float crack  = noise * Mathf.Exp(-progress * 65f) * 0.2f;

                // Envolvente: ataque de 3 ms, caída exponencial suave
                float attack = Mathf.Min(1f, t / 0.003f);
                float env    = attack * Mathf.Exp(-progress * 9.5f);

                // Mezcla + saturación suave (da punch sin clipar duro)
                float s = (wave * 0.65f + crack) * env;
                s = s / (1f + Mathf.Abs(s) * 0.4f);

                data[i] = Mathf.Clamp(s, -1f, 1f);
            }

            var clip = AudioClip.Create("LaserShot", samples, 1, RATE, false);
            clip.SetData(data, 0);
            return clip;
        }

        // ── HUD — copia exacta del FPV del menu (Menú) ──────────
        void BuildHUD()
        {
            var canvasGO = new GameObject("ShooterCanvas");
            canvasGO.transform.SetParent(transform);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            shooterHudCanvas = canvas;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGO.AddComponent<GraphicRaycaster>();

            // Colores identicos al menu
            var CYAN   = new Color(0.05f, 1.00f, 0.08f, 1.00f);
            var ACCENT = new Color(0.05f, 1.00f, 0.08f, 1.00f);
            var WARN   = new Color(1.00f, 0.82f, 0.00f, 1.00f);

            // Metodo local: barra de imagen (mismo que MakeHUDBar del menu)
            System.Action<Transform, UnityEngine.Vector2, UnityEngine.Vector2, float> HBar =
                (parent, pos, size, alpha) => {
                    var g = new GameObject("HBar");
                    g.transform.SetParent(parent, false);
                    var img = g.AddComponent<UnityEngine.UI.Image>(); img.raycastTarget = false;
                    img.color = new Color(CYAN.r, CYAN.g, CYAN.b, alpha);
                    var rt = g.GetComponent<RectTransform>();
                    rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = pos; rt.sizeDelta = size;
                };

            // ── FPV Corner brackets (BuildCornerBrackets del menu) ────────────
            float panelW = 680f, panelH = 680f;
            float cxO = panelW * .5f + 38f, cyO = panelH * .5f + 38f; // 378
            // ── Mira cuadrada: contenedor agrupado (igual que menu, escala reducida) ─
            const float M_R   = 42f;   // radio del cuadrado — encuadra el rombo (D_R=35)
            const float M_LEN = 18f;   // longitud de brazo de esquina
            const float M_THK = 4f;    // grosor de línea
            const float M_A   = 1.00f; // opacidad

            var miraGO = new GameObject("MiraContainer");
            miraGO.transform.SetParent(canvasGO.transform, false);
            miraContainer = miraGO.AddComponent<RectTransform>();
            miraContainer.anchorMin = miraContainer.anchorMax = new Vector2(0.5f, 0.5f);
            miraContainer.pivot = new Vector2(0.5f, 0.5f);
            miraContainer.sizeDelta = Vector2.zero;
            miraContainer.anchoredPosition = Vector2.zero;
            miraGlow = miraGO.AddComponent<CanvasGroup>();

            // Texto ACQUIRING TARGET en la mira
            var maqGO = new GameObject("MiraAcqTxt");
            maqGO.transform.SetParent(miraGO.transform, false);
            miraAcqText = maqGO.AddComponent<TextMeshProUGUI>();
            miraAcqText.text = "ACQUIRING TARGET"; miraAcqText.fontSize = 9f;
            miraAcqText.fontStyle = FontStyles.Bold;
            miraAcqText.color = new Color(1f, 0.82f, 0f, 0f);
            miraAcqText.alignment = TextAlignmentOptions.Center;
            miraAcqText.characterSpacing = 16f; miraAcqText.raycastTarget = false;
            var maqRt = maqGO.GetComponent<RectTransform>();
            maqRt.anchorMin = maqRt.anchorMax = new Vector2(0.5f, 0.5f);
            maqRt.pivot = new Vector2(0.5f, 0.5f);
            maqRt.sizeDelta = new Vector2(220f, 18f);
            maqRt.anchoredPosition = new Vector2(0f, -(55f + 18f));
            maqGO.SetActive(false);

            System.Func<UnityEngine.Vector2, float, float, RectTransform> MakeCorner =
                (pos, hDir, vDir) => {
                    var g = new GameObject("FPV_Corner");
                    g.transform.SetParent(miraGO.transform, false);
                    var rt = g.AddComponent<RectTransform>();
                    rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = pos; rt.sizeDelta = Vector2.zero;
                    HBar(g.transform, new Vector2(hDir * M_LEN * .5f, 0), new Vector2(M_LEN, M_THK), M_A);
                    HBar(g.transform, new Vector2(0, vDir * M_LEN * .5f), new Vector2(M_THK, M_LEN), M_A);
                    return rt;
                };

            fpvCornerTL = MakeCorner(new Vector2(-M_R,  M_R),  1f, -1f);
            fpvCornerTR = MakeCorner(new Vector2( M_R,  M_R), -1f, -1f);
            fpvCornerBL = MakeCorner(new Vector2(-M_R, -M_R),  1f,  1f);
            fpvCornerBR = MakeCorner(new Vector2( M_R, -M_R), -1f,  1f);

            // Solo esquinas — sin ticks, mid-bars ni guide-crosses

            miraBars = miraGO.GetComponentsInChildren<Image>();
            // Recuadro de misil en rojo
            foreach (var mb in miraBars) if (mb) mb.color = new Color(1f, 0.15f, 0.15f, mb.color.a);

            // ── Status labels (BuildStatusLabels del menu) ────────────────────
            System.Func<Transform, UnityEngine.Vector2, string, UnityEngine.Vector2, float, TMPro.TextMeshProUGUI> MakeTelLbl =
                (parent, pos, text, size, fontSize) => {
                    var g = new GameObject("Tel");
                    g.transform.SetParent(parent, false);
                    var tmp = g.AddComponent<TMPro.TextMeshProUGUI>();
                    tmp.text = text; tmp.fontSize = fontSize; tmp.fontStyle = FontStyles.Bold;
                    tmp.color = new Color(CYAN.r, CYAN.g, CYAN.b, 0.88f);
                    tmp.alignment = TextAlignmentOptions.Center; tmp.characterSpacing = 12f;
                    tmp.raycastTarget = false;
                    var rt = g.GetComponent<RectTransform>();
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = pos; rt.sizeDelta = size;
                    return tmp;
                };


            // ── Escalas laterales (BuildFPVBackgroundHUD del menu) ────────────
            BuildFPVScale(canvasGO.transform, -760f, true,  CYAN, out fpvScaleL, out fpvAltText);
            BuildFPVScale(canvasGO.transform,  760f, false, CYAN, out fpvScaleR, out fpvVelText);

            // ── Mira HUD militar elaborada ─────────────────────────────────────
            var chGO = new GameObject("Crosshair");
            chGO.transform.SetParent(canvasGO.transform, false);
            crosshairContainer = chGO.AddComponent<RectTransform>();
            crosshairContainer.anchorMin = crosshairContainer.anchorMax = new Vector2(.5f, .5f);
            crosshairContainer.pivot     = new Vector2(.5f, .5f);
            crosshairContainer.sizeDelta = Vector2.zero;
            crosshairContainer.anchoredPosition = Vector2.zero;
            const float BL=65f, BG=10f, BT=1.8f, VH=18f, TC=10f;
            crosshairBars = new Image[13]; int bi=0;
            // Barras horizontales principales (izq / der)
            crosshairBars[bi++] = MakeBar(chGO.transform, new Vector2(-(BG+BL*.5f), 0f),  new Vector2(BL, BT));
            crosshairBars[bi++] = MakeBar(chGO.transform, new Vector2( (BG+BL*.5f), 0f),  new Vector2(BL, BT));
            // Tapas verticales en extremos de barra — cierran hacia abajo
            crosshairBars[bi++] = MakeBar(chGO.transform, new Vector2(-(BG+BL), -TC*0.5f), new Vector2(BT, TC));
            crosshairBars[bi++] = MakeBar(chGO.transform, new Vector2( (BG+BL), -TC*0.5f), new Vector2(BT, TC));
            // Ticks de referencia intermedios en la barra
            crosshairBars[bi++] = MakeBar(chGO.transform, new Vector2(-(BG+BL*.5f-18f),0f), new Vector2(BT, TC*.55f));
            crosshairBars[bi++] = MakeBar(chGO.transform, new Vector2( (BG+BL*.5f-18f),0f), new Vector2(BT, TC*.55f));
            // Línea vertical superior
            crosshairBars[bi++] = MakeBar(chGO.transform, new Vector2(0f,  VH*.5f),       new Vector2(BT, VH));
            // Línea vertical inferior
            crosshairBars[bi++] = MakeBar(chGO.transform, new Vector2(0f, -VH*.5f),       new Vector2(BT, VH));
            // Tapas horizontales en extremos verticales
            crosshairBars[bi++] = MakeBar(chGO.transform, new Vector2(0f,  VH),           new Vector2(TC, BT));
            crosshairBars[bi++] = MakeBar(chGO.transform, new Vector2(0f, -VH),           new Vector2(TC, BT));
            // Pequeña barra horizontal interior (anchura del visor)
            crosshairBars[bi++] = MakeBar(chGO.transform, new Vector2(0f, 0f),            new Vector2(BG*1.2f, BT));
            // Punto central y referencia
            centerDot            = MakeBar(chGO.transform, Vector2.zero,                   new Vector2(3f, 3f));
            crosshairBars[bi++]  = centerDot;
            crosshairBars[bi++]  = MakeBar(chGO.transform, new Vector2(0f, BG*.6f),       new Vector2(BT*1.5f, BT*1.5f));
            SetCrosshairColor(new Color(0.05f, 1.00f, 0.08f, 1.00f));

            // ── Brujula deslizante (top, dentro del frame) ────────────────────
            var cmpGO = new GameObject("Compass");
            cmpGO.transform.SetParent(canvasGO.transform, false);
            var cmpRt = cmpGO.AddComponent<RectTransform>();
            cmpRt.anchorMin = cmpRt.anchorMax = new Vector2(.5f, .5f); cmpRt.pivot = new Vector2(.5f, .5f);
            cmpRt.anchoredPosition = new Vector2(0f, cyO + 50f); cmpRt.sizeDelta = new Vector2(300f, 30f);
            cmpRt.localScale = Vector3.one * COMPASS_SCALE;
            {
                var idxGO = new GameObject("Idx");
                idxGO.transform.SetParent(cmpGO.transform, false);
                var idxTmp = idxGO.AddComponent<TextMeshProUGUI>();
                idxTmp.text = "▼"; idxTmp.fontSize = 7f; idxTmp.raycastTarget = false;
                idxTmp.color = new Color(CYAN.r, CYAN.g, CYAN.b, 0.94f); idxTmp.alignment = TextAlignmentOptions.Center;
                var idxRt = idxGO.GetComponent<RectTransform>();
                idxRt.anchorMin = idxRt.anchorMax = new Vector2(.5f, 0f); idxRt.pivot = new Vector2(.5f, 0f);
                idxRt.anchoredPosition = new Vector2(0f, -14f); idxRt.sizeDelta = new Vector2(14f, 12f);
            }
            {
                var hv = new GameObject("HdgVal"); hv.transform.SetParent(canvasGO.transform, false);
                headingTmp = hv.AddComponent<TextMeshProUGUI>();
                headingTmp.text = "000"; headingTmp.fontSize = 15f; headingTmp.fontStyle = FontStyles.Bold;
                headingTmp.color = new Color(CYAN.r, CYAN.g, CYAN.b, 0.90f); headingTmp.alignment = TextAlignmentOptions.Center;
                headingTmp.raycastTarget = false;
                var hvRt = hv.GetComponent<RectTransform>();
                hvRt.anchorMin = hvRt.anchorMax = new Vector2(.5f, .5f); hvRt.pivot = new Vector2(.5f, .5f);
                hvRt.anchoredPosition = new Vector2(0f, cyO + 8f); hvRt.sizeDelta = new Vector2(80f, 24f);
                hvRt.localScale = Vector3.one * COMPASS_SCALE;
            }
            for (int ti = 0; ti < COMPASS_TICKS; ti++) {
                var tGO = new GameObject($"CT{ti}"); tGO.transform.SetParent(cmpGO.transform, false);
                var tRt = tGO.AddComponent<RectTransform>();
                tRt.anchorMin = tRt.anchorMax = new Vector2(.5f, 1f); tRt.pivot = new Vector2(.5f, 1f);
                tRt.sizeDelta = Vector2.zero; compassTickRts[ti] = tRt;
                HBar(tGO.transform, new Vector2(0f, -9f), new Vector2(2.5f, 6f), 1.00f);
                var tLGO = new GameObject("L"); tLGO.transform.SetParent(tGO.transform, false);
                var tTmp = tLGO.AddComponent<TextMeshProUGUI>();
                tTmp.fontSize = 13f; tTmp.color = new Color(CYAN.r,CYAN.g,CYAN.b,1.00f);
                tTmp.alignment = TextAlignmentOptions.Center; tTmp.raycastTarget = false;
                var tLRt = tLGO.GetComponent<RectTransform>();
                tLRt.anchorMin = tLRt.anchorMax = new Vector2(.5f, 1f); tLRt.pivot = new Vector2(.5f, 1f);
                tLRt.anchoredPosition = new Vector2(0f, -17f); tLRt.sizeDelta = new Vector2(28f, 11f);
                compassTickTmps[ti] = tTmp;
            }

            // ── Pitch ladder ──────────────────────────────────────────────────
            {
                // Màscara per retallar les línies fora de la zona central
                var maskGO = new GameObject("PitchMask");
                maskGO.transform.SetParent(canvasGO.transform, false);
                maskGO.AddComponent<RectMask2D>();
                var maskRt = maskGO.GetComponent<RectTransform>();
                maskRt.anchorMin = maskRt.anchorMax = new Vector2(0.5f, 0.5f);
                maskRt.pivot     = new Vector2(0.5f, 0.5f);
                maskRt.sizeDelta = new Vector2(900f, 420f);
                maskRt.anchoredPosition = Vector2.zero;

                // Arrel mòbil: es desplaça verticalment amb el pitch
                var rootGO = new GameObject("PitchRoot");
                rootGO.transform.SetParent(maskGO.transform, false);
                pitchLadderRoot = rootGO.AddComponent<RectTransform>();
                pitchLadderRoot.anchorMin = pitchLadderRoot.anchorMax = new Vector2(0.5f, 0.5f);
                pitchLadderRoot.pivot     = new Vector2(0.5f, 0.5f);
                pitchLadderRoot.sizeDelta = Vector2.zero;
                pitchLadderRoot.anchoredPosition = Vector2.zero;

                const float GAP   = 80f;   // forat central (per no tapar la mira)
                const float MAJ_W = 96f;   // longitud de cada braç
                const float LH    = 1.5f;  // alçada de la línia horitzontal
                const float CAP_H = 10f;   // alçada del bracket de tancament

                // Helper per crear un segment de línia
                System.Action<Transform, Vector2, Vector2, Color> Seg = (par, pos, sz, col) => {
                    var g = new GameObject("S"); g.transform.SetParent(par, false);
                    var im = g.AddComponent<Image>(); im.color = col; im.raycastTarget = false;
                    var rt = g.GetComponent<RectTransform>();
                    rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = pos; rt.sizeDelta = sz;
                };

                for (int deg = -90; deg <= 90; deg += 10)
                {
                    if (deg == 0) continue;
                    float y      = deg * PITCH_PX_DEG;
                    Color lc     = new Color(CYAN.r, CYAN.g, CYAN.b, 0.82f);
                    // bracket apunta cap a l'horitzó (0°): positiu→avall, negatiu→amunt
                    float capDir = deg > 0 ? -1f : 1f;
                    float capY   = y + capDir * CAP_H * 0.5f;

                    // Braç esquerre + bracket exterior
                    Seg(rootGO.transform, new Vector2(-(GAP + MAJ_W * 0.5f), y),   new Vector2(MAJ_W, LH),   lc);
                    Seg(rootGO.transform, new Vector2(-(GAP + MAJ_W),         capY), new Vector2(LH,    CAP_H), lc);

                    // Braç dret + bracket exterior
                    Seg(rootGO.transform, new Vector2( (GAP + MAJ_W * 0.5f), y),   new Vector2(MAJ_W, LH),   lc);
                    Seg(rootGO.transform, new Vector2( (GAP + MAJ_W),         capY), new Vector2(LH,    CAP_H), lc);

                    // Etiquetes de grau
                    foreach (float sx in new float[] { -(GAP + MAJ_W + 18f), (GAP + MAJ_W + 18f) })
                    {
                        var lblGO = new GameObject($"PL_N{deg}"); lblGO.transform.SetParent(rootGO.transform, false);
                        var lbl   = lblGO.AddComponent<TextMeshProUGUI>();
                        lbl.text  = $"{Mathf.Abs(deg)}°"; lbl.fontSize = 14f;
                        lbl.fontStyle = FontStyles.Bold; lbl.raycastTarget = false;
                        lbl.color = new Color(CYAN.r, CYAN.g, CYAN.b, 0.75f);
                        lbl.alignment = sx < 0 ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;
                        var lblRt = lblGO.GetComponent<RectTransform>();
                        lblRt.anchorMin = lblRt.anchorMax = lblRt.pivot = new Vector2(0.5f, 0.5f);
                        lblRt.anchoredPosition = new Vector2(sx, y);
                        lblRt.sizeDelta = new Vector2(28f, 14f);
                    }
                }

            }

            // ── Designadores de blanco (MAX_IND = 30 = MAX_COUNT del spawner) ──────
            for (int ii = 0; ii < MAX_IND; ii++) {
                var iGO = new GameObject($"T{ii}"); iGO.transform.SetParent(canvasGO.transform, false);
                var iRt = iGO.AddComponent<RectTransform>();
                iRt.anchorMin = iRt.anchorMax = new Vector2(.5f,.5f); iRt.pivot = new Vector2(.5f,.5f);
                iRt.sizeDelta = Vector2.zero;
                const float D_R = 35f, D_THK = 3f;
                {
                    var dsGO = new GameObject("DS"); dsGO.transform.SetParent(iGO.transform, false);
                    var dsImg = dsGO.AddComponent<Image>(); dsImg.raycastTarget = false;
                    dsImg.color  = new Color(1f, 0.15f, 0.15f, 1.00f);
                    dsImg.sprite = BuildDiamondSprite(D_R, D_THK);
                    var dsRt = dsGO.GetComponent<RectTransform>();
                    dsRt.anchorMin = dsRt.anchorMax = dsRt.pivot = new Vector2(0.5f, 0.5f);
                    dsRt.anchoredPosition = Vector2.zero;
                    dsRt.sizeDelta = new Vector2(D_R * 2f, D_R * 2f);
                }
                indContainers[ii] = iRt; iGO.SetActive(false);
                var dGO = new GameObject($"D{ii}"); dGO.transform.SetParent(canvasGO.transform, false);
                var dTmp = dGO.AddComponent<TextMeshProUGUI>();
                dTmp.fontSize=14f; dTmp.fontStyle=FontStyles.Bold; dTmp.color=new Color(1.00f,0.00f,0.00f,1.00f);
                dTmp.alignment=TextAlignmentOptions.Center; dTmp.raycastTarget=false;
                var dRt=dGO.GetComponent<RectTransform>();
                dRt.anchorMin=dRt.anchorMax=new Vector2(.5f,.5f); dRt.pivot=new Vector2(.5f,.5f); dRt.sizeDelta=new Vector2(60f,14f);
                indDistTmps[ii]=dTmp; dGO.SetActive(false);
                var aqGO=new GameObject($"AQ{ii}"); aqGO.transform.SetParent(canvasGO.transform, false);
                var aqTmp=aqGO.AddComponent<TextMeshProUGUI>();
                aqTmp.text="ACQUIRING TARGET"; aqTmp.fontSize=9f; aqTmp.fontStyle=FontStyles.Bold;
                aqTmp.color=new Color(WARN.r,WARN.g,WARN.b,0.20f); aqTmp.alignment=TextAlignmentOptions.Center; aqTmp.characterSpacing=16f;
                aqTmp.raycastTarget=false;
                var aqRt=aqGO.GetComponent<RectTransform>();
                aqRt.anchorMin=aqRt.anchorMax=new Vector2(.5f,.5f); aqRt.pivot=new Vector2(.5f,.5f); aqRt.sizeDelta=new Vector2(220f,18f);
                indAcqTmps[ii]=aqTmp; aqGO.SetActive(false);

                // ── Icono de mira (círculo + cruz + punto) al lado de TARGET LOCKED
                const float IC_R=5f, IC_THK=1.5f, IC_LINE=4f; const int IC_SEG=16;
                var icGO=new GameObject($"LI{ii}"); icGO.transform.SetParent(canvasGO.transform,false);
                var icRt=icGO.AddComponent<RectTransform>();
                icRt.anchorMin=icRt.anchorMax=icRt.pivot=new Vector2(.5f,.5f); icRt.sizeDelta=Vector2.zero;
                Color icC=new Color(1f,0.15f,0.15f,1f);
                for(int s=0;s<IC_SEG;s++){
                    float sdeg=s*360f/IC_SEG, srad=sdeg*Mathf.Deg2Rad;
                    float arcL=2f*Mathf.PI*IC_R/IC_SEG*1.2f;
                    var sg=new GameObject("S"); sg.transform.SetParent(icGO.transform,false);
                    var sI=sg.AddComponent<Image>(); sI.raycastTarget=false; sI.color=icC;
                    var sR=sg.GetComponent<RectTransform>();
                    sR.anchorMin=sR.anchorMax=sR.pivot=new Vector2(.5f,.5f);
                    sR.anchoredPosition=new Vector2(Mathf.Cos(srad)*IC_R,Mathf.Sin(srad)*IC_R);
                    sR.sizeDelta=new Vector2(IC_THK,arcL); sR.localRotation=Quaternion.Euler(0f,0f,sdeg);
                }
                Vector2[] icLP={new Vector2(0f,IC_R+IC_LINE*.5f),new Vector2(0f,-(IC_R+IC_LINE*.5f)),
                                 new Vector2(-(IC_R+IC_LINE*.5f),0f),new Vector2(IC_R+IC_LINE*.5f,0f)};
                for(int li=0;li<4;li++){
                    var lg=new GameObject("L"); lg.transform.SetParent(icGO.transform,false);
                    var lI=lg.AddComponent<Image>(); lI.raycastTarget=false; lI.color=icC;
                    var lR=lg.GetComponent<RectTransform>();
                    lR.anchorMin=lR.anchorMax=lR.pivot=new Vector2(.5f,.5f);
                    lR.anchoredPosition=icLP[li];
                    lR.sizeDelta=li<2?new Vector2(IC_THK,IC_LINE):new Vector2(IC_LINE,IC_THK);
                }
                var dtG=new GameObject("D"); dtG.transform.SetParent(icGO.transform,false);
                var dtI=dtG.AddComponent<Image>(); dtI.raycastTarget=false; dtI.color=icC;
                var dtR=dtG.GetComponent<RectTransform>();
                dtR.anchorMin=dtR.anchorMax=dtR.pivot=new Vector2(.5f,.5f);
                dtR.anchoredPosition=Vector2.zero; dtR.sizeDelta=new Vector2(2.5f,2.5f);
                indLockIcons[ii]=icRt; icGO.SetActive(false);
            }

            // ── Barra de salut del dron (superior esquerra, alineada amb ALT i brúixola) ──
            {
                const float BAR_W = 160f, BAR_H = 14f;
                // Fons fosc
                var hBgGO = new GameObject("HealthBg");
                hBgGO.transform.SetParent(canvasGO.transform, false);
                hBgGO.AddComponent<Image>().color = new Color(0.05f, 0.07f, 0.12f, 0.70f);
                var hBgRt = hBgGO.GetComponent<RectTransform>();
                hBgRt.anchorMin = hBgRt.anchorMax = new Vector2(0f, 1f);
                hBgRt.pivot = new Vector2(0f, 1f);
                hBgRt.anchoredPosition = new Vector2(290f, -97f);
                hBgRt.sizeDelta = new Vector2(BAR_W, BAR_H);

                // Relleno — rect que se encoge por la derecha según el % de vida
                var hFillGO = new GameObject("HealthFill");
                hFillGO.transform.SetParent(hBgGO.transform, false);
                healthBarFill = hFillGO.AddComponent<Image>();
                healthBarFill.color       = new Color(0.05f, 1.00f, 0.08f, 0.90f);
                healthBarFill.raycastTarget = false;
                var hFillRt = hFillGO.GetComponent<RectTransform>();
                hFillRt.anchorMin = new Vector2(0f, 0f);
                hFillRt.anchorMax = new Vector2(1f, 1f);   // anchorMax.x = pct en TakeDamage
                hFillRt.offsetMin = new Vector2(1f, 1f);
                hFillRt.offsetMax = new Vector2(-1f, -1f);

                // Porcentaje a la derecha de la barra
                var hPctGO = new GameObject("HealthPct");
                hPctGO.transform.SetParent(canvasGO.transform, false);
                healthPctTmp = hPctGO.AddComponent<TextMeshProUGUI>();
                healthPctTmp.text      = "100%";
                healthPctTmp.fontSize  = 12f;
                healthPctTmp.fontStyle = FontStyles.Bold;
                healthPctTmp.color     = new Color(0.05f, 1.00f, 0.08f, 0.90f);
                healthPctTmp.alignment = TextAlignmentOptions.Left;
                healthPctTmp.raycastTarget = false;
                var hPctRt = hPctGO.GetComponent<RectTransform>();
                hPctRt.anchorMin = hPctRt.anchorMax = new Vector2(0f, 1f);
                hPctRt.pivot = new Vector2(0f, 1f);
                hPctRt.anchoredPosition = new Vector2(290f + BAR_W + 6f, -97f);
                hPctRt.sizeDelta = new Vector2(40f, BAR_H);
            }

            // ── Impactos + Arma (superior dreta, alineats amb VEL i brúixola) ──
            // Apilados verticalmente con el mismo borde derecho
            const float COL_X = -240f;
            {
                // Arma seleccionada — texto: "LASER" / "MISSILE"
                var wGO = new GameObject("WeaponLabel"); wGO.transform.SetParent(canvasGO.transform, false);
                weaponLabel = wGO.AddComponent<TextMeshProUGUI>();
                weaponLabel.text = "LASER"; weaponLabel.fontSize = 14f;
                weaponLabel.fontStyle = FontStyles.Bold; weaponLabel.raycastTarget = false;
                weaponLabel.color = new Color(CYAN.r, CYAN.g, CYAN.b, 0.90f);
                weaponLabel.alignment = TextAlignmentOptions.Left;   // izquierda: sin espacio muerto
                var wRt = wGO.GetComponent<RectTransform>();
                wRt.anchorMin = wRt.anchorMax = new Vector2(1f, 1f); wRt.pivot = new Vector2(1f, 1f);
                wRt.anchoredPosition = new Vector2(COL_X, -97f); wRt.sizeDelta = new Vector2(105f, 20f);

                // ── Icono procedural: cruz (laser) / mira (misil) ─────────────
                const float ICO = 18f;    // tamaño del contenedor cuadrado
                const float IR  = 7.5f;   // radio del anillo
                const float ITH = 1.5f;   // grosor de líneas

                // Icono pegado al borde izquierdo del texto (que ahora empieza en ese borde)
                var icoGO = new GameObject("WeaponIcon"); icoGO.transform.SetParent(canvasGO.transform, false);
                var icoRt = icoGO.AddComponent<RectTransform>();
                icoRt.anchorMin = icoRt.anchorMax = new Vector2(1f, 1f); icoRt.pivot = new Vector2(1f, 1f);
                icoRt.anchoredPosition = new Vector2(COL_X - 105f - 4f, -97f); icoRt.sizeDelta = new Vector2(ICO, ICO);

                Color wCol = new Color(CYAN.r, CYAN.g, CYAN.b, 0.90f);

                // Anillo — solo se muestra en modo misil
                var ringGO = new GameObject("Ring"); ringGO.transform.SetParent(icoGO.transform, false);
                ringGO.AddComponent<RectTransform>().sizeDelta = Vector2.zero;
                weaponRingRoot = ringGO;
                const int SEG = 20;
                for (int si = 0; si < SEG; si++)
                {
                    float sdeg = si * 360f / SEG;
                    float srad = sdeg * Mathf.Deg2Rad;
                    float arcL = 2f * Mathf.PI * IR / SEG;
                    var sg   = new GameObject("S"); sg.transform.SetParent(ringGO.transform, false);
                    var sImg = sg.AddComponent<Image>(); sImg.color = wCol; sImg.raycastTarget = false;
                    var sRt  = sg.GetComponent<RectTransform>();
                    sRt.anchorMin = sRt.anchorMax = sRt.pivot = new Vector2(0.5f, 0.5f);
                    sRt.anchoredPosition = new Vector2(Mathf.Cos(srad) * IR, Mathf.Sin(srad) * IR);
                    sRt.sizeDelta        = new Vector2(ITH, arcL * 1.25f);
                    sRt.localRotation    = Quaternion.Euler(0f, 0f, sdeg);
                }
                ringGO.SetActive(true);    // visible en ambos modos

                // Cruz — siempre visible (barra horizontal + barra vertical)
                var cH = MakeBar(icoGO.transform, Vector2.zero, new Vector2(IR * 2f - ITH, ITH)); cH.color = wCol;
                var cV = MakeBar(icoGO.transform, Vector2.zero, new Vector2(ITH, IR * 2f - ITH)); cV.color = wCol;

                // Cuenta atrás del misil — misma fila que weaponLabel, alineada a la derecha
                var cdGO = new GameObject("MissileCooldown"); cdGO.transform.SetParent(canvasGO.transform, false);
                cooldownTmp = cdGO.AddComponent<TextMeshProUGUI>();
                cooldownTmp.fontSize = 13f; cooldownTmp.fontStyle = FontStyles.Bold;
                cooldownTmp.color = new Color(0.05f, 1.00f, 0.08f, 0.95f);   // naranja
                cooldownTmp.alignment = TextAlignmentOptions.Right;
                cooldownTmp.raycastTarget = false;
                var cdRt = cdGO.GetComponent<RectTransform>();
                cdRt.anchorMin = cdRt.anchorMax = new Vector2(1f, 1f); cdRt.pivot = new Vector2(1f, 1f);
                cdRt.anchoredPosition = new Vector2(COL_X, -97f); cdRt.sizeDelta = new Vector2(105f, 20f);
                cdGO.SetActive(false);

                // Impactos (segunda fila, desplazado a la izquierda)
                var kVGO = new GameObject("KV"); kVGO.transform.SetParent(canvasGO.transform, false);
                killCounterTmp = kVGO.AddComponent<TextMeshProUGUI>();
                killCounterTmp.text = "DERRIBOS  0"; killCounterTmp.fontSize = 14f; killCounterTmp.fontStyle = FontStyles.Bold;
                killCounterTmp.raycastTarget = false;
                killCounterTmp.color = new Color(CYAN.r, CYAN.g, CYAN.b, 0.90f); killCounterTmp.alignment = TextAlignmentOptions.Right;
                var kVRt = kVGO.GetComponent<RectTransform>();
                kVRt.anchorMin = kVRt.anchorMax = new Vector2(1f, 1f); kVRt.pivot = new Vector2(1f, 1f);
                kVRt.anchoredPosition = new Vector2(COL_X - 160f, -97f); kVRt.sizeDelta = new Vector2(180f, 20f);
            }

            // Flash de impacto recibido
            {
                var hovGO=new GameObject("HitOverlay"); hovGO.transform.SetParent(canvasGO.transform,false);
                hitOverlayImg=hovGO.AddComponent<Image>();
                hitOverlayImg.color=new Color(1f,0f,0f,0f);
                var hovRt=hovGO.GetComponent<RectTransform>();
                hovRt.anchorMin=Vector2.zero; hovRt.anchorMax=Vector2.one;
                hovRt.sizeDelta=Vector2.zero; hovRt.anchoredPosition=Vector2.zero;
                hovGO.SetActive(false);
            }

            RegisterHudTinted(canvasGO.transform);
            AddHudGlow(canvasGO.transform);
            ApplyHudColor();
        }

        // ── Glow ─────────────────────────────────────────────────────────────
        // Sobre fondos claros (cielo, nubes) un trazo fino se lava y pierde
        // contraste. En vez de tocar su ancho o su verde, se añade detrás un
        // halo fino y difuminado del mismo color (aclarado hacia blanco) que
        // se ciñe al contorno de cada elemento — no un bloque rectangular
        // suelto — y se lee como luz emitida por la línea.
        const float GLOW_PAD = 1.6f, GLOW_ALPHA_MUL = 0.95f, GLOW_WHITE_MIX = 0.25f;
        static Sprite _glowSprite;

        // ── Rombo del designador (contorno) ────────────────────────────────────
        // Un rombo con vértices a distancia R del centro es exactamente la forma
        // |x|+|y| <= R (distancia Manhattan/L1) — y el contorno de grosor
        // perpendicular uniforme T de ese rombo es el anillo |x|+|y| entre
        // R - T*sqrt(2) y R. Es la misma figura, con el mismo grosor real, que
        // dibujar 4 barras rotadas ±45° formando los 4 lados — pero aquí es un
        // único sprite procedural en vez de 4 GameObjects.
        static Sprite _diamondSprite;

        static Sprite BuildDiamondSprite(float outerR, float thickness)
        {
            if (_diamondSprite != null) return _diamondSprite;
            const int size = 64;
            float innerR = outerR - thickness * 1.41421356f;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float nx = ((x + 0.5f) / size * 2f - 1f) * outerR;
                    float ny = ((y + 0.5f) / size * 2f - 1f) * outerR;
                    float r  = Mathf.Abs(nx) + Mathf.Abs(ny);
                    float a  = (r >= innerR && r <= outerR) ? 1f : 0f;
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(true);
            tex.filterMode = FilterMode.Bilinear;
            _diamondSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return _diamondSprite;
        }

        // Mancha difuminada (sin meseta plana: el degradado ocupa todo el
        // sprite) para el halo de las líneas/barras rectangulares simples.
        static Sprite BuildGlowSprite()
        {
            if (_glowSprite != null) return _glowSprite;
            const int size = 16;
            const float feather = size * 0.5f;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Min(Mathf.Min(x, size - 1 - x), Mathf.Min(y, size - 1 - y));
                    float t = Mathf.Clamp01(d / feather); // 0 en el borde, 1 en el centro

                    // Caída tipo gaussiana en vez de un smoothstep: plateau
                    // suave junto al trazo y una cola larga y progresiva hacia
                    // fuera, en lugar de un corte más brusco — es lo que da el
                    // aspecto de neón (luz que se apaga poco a poco).
                    float a    = Mathf.Exp(-4f * (1f - t) * (1f - t));
                    byte  core = (byte)Mathf.Lerp(210f, 255f, t); // núcleo algo más caliente/blanco

                    px[y * size + x] = new Color32(core, core, core, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(true);
            tex.filterMode = FilterMode.Bilinear;
            _glowSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return _glowSprite;
        }

        static void AddHudGlow(Transform root)
        {
            var blobSprite = BuildGlowSprite();
            foreach (var core in root.GetComponentsInChildren<Image>(true))
            {
                var glowGO = new GameObject("Glow");
                glowGO.transform.SetParent(core.transform.parent, false);
                var glow = glowGO.AddComponent<Image>();
                glow.raycastTarget = false;

                var coreRt = core.rectTransform;
                var glowRt = glowGO.GetComponent<RectTransform>();
                glowRt.anchorMin = coreRt.anchorMin; glowRt.anchorMax = coreRt.anchorMax; glowRt.pivot = coreRt.pivot;
                glowRt.anchoredPosition = coreRt.anchoredPosition;

                if (core.sprite != null)
                {
                    // Iconos con forma propia (p. ej. las cajas ALT/VEL): el halo
                    // reutiliza el mismo sprite, sólo un poco más grande, para
                    // que siga exactamente su silueta en vez de asomar como un
                    // bloque rectangular por detrás.
                    glow.sprite = core.sprite;
                    glow.type   = core.type;
                    glowRt.sizeDelta = coreRt.sizeDelta + new Vector2(GLOW_PAD, GLOW_PAD) * 2f;
                }
                else
                {
                    glow.sprite = blobSprite;
                    glow.type   = Image.Type.Simple;
                    glowRt.sizeDelta = coreRt.sizeDelta + new Vector2(GLOW_PAD * 2f, GLOW_PAD * 2f);
                }

                glowGO.transform.SetSiblingIndex(core.transform.GetSiblingIndex()); // detrás del trazo

                var link = glowGO.AddComponent<HudGlowLink>();
                link.core = core; link.glow = glow;
                link.Sync();
            }

            foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
            {
                // Outline de TMP: el shader lo soporta pero lo mantiene apagado
                // por keyword salvo que se active explícitamente (el material
                // por defecto del proyecto no la trae activada de fábrica).
                t.outlineWidth = 0.20f;
                t.outlineColor = Color.Lerp(t.color, Color.white, 0.35f);
                t.fontMaterial.EnableKeyword("OUTLINE_ON");
            }
        }

        // Mantiene el halo sincronizado cuando el trazo cambia de color en vivo
        // (parpadeos, impactos, cambios de estado de la mira o la salud).
        class HudGlowLink : MonoBehaviour
        {
            public Image core, glow;
            Color lastCoreColor;

            void LateUpdate()
            {
                if (core == null || glow == null) { enabled = false; return; }
                if (core.color == lastCoreColor) return;
                Sync();
            }

            public void Sync()
            {
                lastCoreColor = core.color;
                var tinted = Color.Lerp(lastCoreColor, Color.white, GLOW_WHITE_MIX);
                glow.color  = new Color(tinted.r, tinted.g, tinted.b, lastCoreColor.a * GLOW_ALPHA_MUL);
            }
        }

        // Contorno de un pentágono: rectángulo con un pico apuntando a un lado (5 lados), solo líneas.
        Sprite BuildTagShapeSprite(bool pointRight)
        {
            if (pointRight && _tagShapeSpriteRight != null) return _tagShapeSpriteRight;
            if (!pointRight && _tagShapeSpriteLeft  != null) return _tagShapeSpriteLeft;

            const int texW = 256, texH = 96;
            const float pointFrac = 0.22f;
            const float aa = 2.5f;
            const float thickness = 10f;
            float pointW = texW * pointFrac;
            float halfH  = texH * 0.5f;

            var tex = new Texture2D(texW, texH, TextureFormat.RGBA32, true);
            tex.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[texW * texH];
            for (int y = 0; y < texH; y++)
            {
                for (int x = 0; x < texW; x++)
                {
                    float xx = pointRight ? (texW - 1 - x) : x;

                    float dTop, dBot;
                    if (xx >= pointW)
                    {
                        dTop = texH - y; dBot = y;
                    }
                    else
                    {
                        float t = xx / pointW;
                        dTop = Mathf.Lerp(halfH, texH, t) - y;
                        dBot = y - Mathf.Lerp(halfH, 0f, t);
                    }
                    float dOpen = pointRight ? x : (texW - x);
                    float d = Mathf.Min(dTop, Mathf.Min(dBot, dOpen));

                    float outerFade = Mathf.Clamp01((d + aa * 0.5f) / aa);
                    float innerFade = 1f - Mathf.Clamp01((d - thickness + aa * 0.5f) / aa);
                    float alpha = Mathf.Clamp01(Mathf.Min(outerFade, innerFade));

                    pixels[y * texW + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(true);
            tex.filterMode = FilterMode.Trilinear;

            var sprite = Sprite.Create(tex, new Rect(0, 0, texW, texH),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            if (pointRight) _tagShapeSpriteRight = sprite; else _tagShapeSpriteLeft = sprite;
            return sprite;
        }

        void BuildFPVScale(Transform parent, float xPos, bool isLeft, Color cyan,
            out RectTransform tickRoot, out TextMeshProUGUI valLabel)
        {
            const float YSTEP = 56f;
            const int   HALF  = 20;
            const float CLIPH = YSTEP * 12f;   // 672f
            float rOff = isLeft ?  36f : -36f;  // rail offset reducido
            float tDir = isLeft ?   1f :  -1f;
            float lbX  = isLeft ? -20f :  20f;
            float mW   = 20f, mnW = 10f;        // ticks más estrechos
            string side = isLeft ? "L" : "R";
            TextAlignmentOptions ta = isLeft ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;

            var sgGO = new GameObject($"ScaleGroup_{side}");
            sgGO.transform.SetParent(parent, false);
            sgGO.AddComponent<RectMask2D>();
            var sgRt = sgGO.GetComponent<RectTransform>();
            sgRt.anchorMin = sgRt.anchorMax = sgRt.pivot = new Vector2(0.5f, 0.5f);
            sgRt.anchoredPosition = new Vector2(xPos, 0f);
            sgRt.sizeDelta = new Vector2(100f, CLIPH);  // suficiente para ticks + etiquetas

            {
                var g = new GameObject("Rail"); g.transform.SetParent(sgGO.transform, false);
                var img = g.AddComponent<Image>(); img.raycastTarget = false;
                img.color = new Color(cyan.r, cyan.g, cyan.b, 1.00f);
                var rt = g.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(rOff, 0f);
                rt.sizeDelta = new Vector2(2.5f, CLIPH);  // igual que el menú (railThk)
            }

            var tGO = new GameObject($"TickRoot_{side}"); tGO.transform.SetParent(sgGO.transform, false);
            var tRt = tGO.AddComponent<RectTransform>();
            tRt.anchorMin = tRt.anchorMax = tRt.pivot = new Vector2(0.5f, 0.5f);
            tRt.anchoredPosition = Vector2.zero; tRt.sizeDelta = Vector2.zero;
            tickRoot = tRt;

            for (int i = -(HALF+2); i <= (HALF+2); i++)
            {
                float yy  = i * YSTEP;
                int   val = i;
                bool  maj = val % 5 == 0;
                float tw  = maj ? mW : mnW;

                var bGO = new GameObject("Bar"); bGO.transform.SetParent(tRt, false);
                var bImg = bGO.AddComponent<Image>(); bImg.raycastTarget = false;
                bImg.color = new Color(cyan.r, cyan.g, cyan.b, maj ? 1.00f : 0.70f);
                var bRt = bGO.GetComponent<RectTransform>();
                bRt.anchorMin = bRt.anchorMax = bRt.pivot = new Vector2(0.5f, 0.5f);
                bRt.anchoredPosition = new Vector2(rOff + tDir * tw * .5f, yy);
                bRt.sizeDelta = new Vector2(tw, maj ? 2.5f : 2f);  // igual que el menú

                if (maj)
                {
                    var lGO = new GameObject($"ScaleTick_{side}_{i}");
                    lGO.transform.SetParent(tRt, false);
                    var lTmp = lGO.AddComponent<TextMeshProUGUI>(); lTmp.raycastTarget = false;
                    lTmp.text = val.ToString();
                    lTmp.fontSize = 14f;
                    lTmp.color = new Color(cyan.r, cyan.g, cyan.b, val == 0 ? 1.00f : 0.90f);
                    lTmp.fontStyle = FontStyles.Bold; lTmp.alignment = ta;
                    var lRt = lGO.GetComponent<RectTransform>();
                    lRt.anchorMin = lRt.anchorMax = lRt.pivot = new Vector2(0.5f, 0.5f);
                    lRt.anchoredPosition = new Vector2(lbX, yy);
                    lRt.sizeDelta = new Vector2(50f, 18f);
                }
            }

            // Agulla central (indicador de valor actual) — igual que el menú
            {
                var g = new GameObject("Pointer"); g.transform.SetParent(parent, false);
                var img = g.AddComponent<Image>(); img.raycastTarget = false;
                img.color = new Color(cyan.r, cyan.g, cyan.b, 1.00f);
                var rt = g.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(xPos + rOff + tDir * (mW + 4f), 0f);
                rt.sizeDelta = new Vector2(mW + 8f, 3f);  // igual que el menú
            }

            // Etiqueta de valor (ALT / VEL) — posicionada como el menú (railX + 90)
            float vx = xPos + rOff + (isLeft ? 90f : -90f);

            if (isLeft)
            {
                // "ALT" como etiqueta fija encima de la caja de valor
                var alGO = new GameObject("AltLabel"); alGO.transform.SetParent(parent, false);
                var alTmp = alGO.AddComponent<TextMeshProUGUI>(); alTmp.raycastTarget = false;
                alTmp.text = "ALT"; alTmp.fontSize = 12f; alTmp.fontStyle = FontStyles.Bold;
                alTmp.color = new Color(cyan.r, cyan.g, cyan.b, 1.00f);
                alTmp.alignment = TextAlignmentOptions.Center; alTmp.characterSpacing = 0f;
                var alRt = alGO.GetComponent<RectTransform>();
                alRt.anchorMin = alRt.anchorMax = alRt.pivot = new Vector2(0.5f, 0.5f);
                alRt.anchoredPosition = new Vector2(vx + 8f, 28f); alRt.sizeDelta = new Vector2(140f, 18f);

                // Caja con pico apuntando a la izquierda alrededor del valor (pentágono, 5 lados)
                // El pico se alinea con la marca central de la escala (y = 0).
                const float BOX_W = 66f, BOX_H = 20f;
                var boxGO = new GameObject("AltValueBox"); boxGO.transform.SetParent(parent, false);
                var boxRt = boxGO.AddComponent<RectTransform>();
                boxRt.anchorMin = boxRt.anchorMax = boxRt.pivot = new Vector2(0.5f, 0.5f);
                boxRt.anchoredPosition = new Vector2(vx, 0f); boxRt.sizeDelta = new Vector2(BOX_W, BOX_H);
                var boxImg = boxGO.AddComponent<Image>();
                boxImg.sprite = BuildTagShapeSprite(pointRight: false); boxImg.type = Image.Type.Simple;
                boxImg.color = new Color(cyan.r, cyan.g, cyan.b, 0.90f); boxImg.raycastTarget = false;

                var valGO = new GameObject("AltValue"); valGO.transform.SetParent(boxGO.transform, false);
                var valTmp = valGO.AddComponent<TextMeshProUGUI>(); valTmp.raycastTarget = false;
                valTmp.text = "--.-"; valTmp.fontSize = 14f; valTmp.fontStyle = FontStyles.Bold;
                valTmp.color = new Color(cyan.r, cyan.g, cyan.b, 1.00f);
                valTmp.alignment = TextAlignmentOptions.Center;
                var valRt = valGO.GetComponent<RectTransform>();
                valRt.anchorMin = Vector2.zero; valRt.anchorMax = Vector2.one;
                valRt.offsetMin = new Vector2(BOX_W * 0.22f, 0f); valRt.offsetMax = Vector2.zero;
                valLabel = valTmp;
            }
            else
            {
                // "VEL" como etiqueta fija encima de la caja de valor
                var vlGO = new GameObject("VelLabel"); vlGO.transform.SetParent(parent, false);
                var vlTmp = vlGO.AddComponent<TextMeshProUGUI>(); vlTmp.raycastTarget = false;
                vlTmp.text = "VEL"; vlTmp.fontSize = 12f; vlTmp.fontStyle = FontStyles.Bold;
                vlTmp.color = new Color(cyan.r, cyan.g, cyan.b, 1.00f);
                vlTmp.alignment = TextAlignmentOptions.Center; vlTmp.characterSpacing = 0f;
                var vlRt = vlGO.GetComponent<RectTransform>();
                vlRt.anchorMin = vlRt.anchorMax = vlRt.pivot = new Vector2(0.5f, 0.5f);
                vlRt.anchoredPosition = new Vector2(vx - 8f, 28f); vlRt.sizeDelta = new Vector2(140f, 18f);

                // Caja con pico apuntando a la derecha alrededor del valor (pentágono, 5 lados)
                // El pico se alinea con la marca central de la escala (y = 0).
                const float BOX_W = 66f, BOX_H = 20f;
                var boxGO = new GameObject("VelValueBox"); boxGO.transform.SetParent(parent, false);
                var boxRt = boxGO.AddComponent<RectTransform>();
                boxRt.anchorMin = boxRt.anchorMax = boxRt.pivot = new Vector2(0.5f, 0.5f);
                boxRt.anchoredPosition = new Vector2(vx, 0f); boxRt.sizeDelta = new Vector2(BOX_W, BOX_H);
                var boxImg = boxGO.AddComponent<Image>();
                boxImg.sprite = BuildTagShapeSprite(pointRight: true); boxImg.type = Image.Type.Simple;
                boxImg.color = new Color(cyan.r, cyan.g, cyan.b, 0.90f); boxImg.raycastTarget = false;

                var vGO = new GameObject("VelValue"); vGO.transform.SetParent(boxGO.transform, false);
                var vTmp = vGO.AddComponent<TextMeshProUGUI>(); vTmp.raycastTarget = false;
                vTmp.text = "0.0"; vTmp.fontSize = 14f; vTmp.fontStyle = FontStyles.Bold;
                vTmp.color = new Color(cyan.r, cyan.g, cyan.b, 1.00f);
                vTmp.alignment = TextAlignmentOptions.Center;
                var vRt = vGO.GetComponent<RectTransform>();
                vRt.anchorMin = Vector2.zero; vRt.anchorMax = Vector2.one;
                vRt.offsetMin = Vector2.zero; vRt.offsetMax = new Vector2(-BOX_W * 0.22f, 0f);
                valLabel = vTmp;
            }
        }



        Image MakeBar(Transform parent, Vector2 pos, Vector2 size)
        {
            var go  = new GameObject("Bar");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            var rt  = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta        = size;
            return img;
        }
    }
}
