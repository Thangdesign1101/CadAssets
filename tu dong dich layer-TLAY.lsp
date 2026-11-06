;;; ==========================================================================
;;; LENH: TLAY (Tich hop FIXLAY + Quet chon vung doi tuong + Auto Dim/Hatch)
;;; - QUET CHON VUNG DOI TUONG: Quet chon vung nao chi xu ly doi tuong trong
;;;   vung do (va cac Block nam trong vung chon), tranh lag ban ve va de kiem soat.
;;; - NGUYEN TAC BAO TOAN TUYET DOI:
;;;   1. Tat ca cac Layer co tien to "DB-" (Layer cong ty) DA CO SAN trong ban ve
;;;      duoc GIU NGUYEN 100% Mau sac, Linetype, Lineweight, Plot (khong ghi de).
;;;      Chi khi ban ve CHUA CO Layer DB- do thi moi tao moi theo bang chuan tren anh.
;;;   2. Rieng doi tuong HATCH va cac Layer HATCH / NSA, GSA, GBA, GFA, CFA, AREA:
;;;      GIU NGUYEN 100% layer va thuoc tinh cuc bo khi copy vao (khong dich, khong
;;;      doi mau, khong ep ByLayer, khong xoa).
;;;   3. Khi thao tac Dim -> Tu dong chuyen ve "DB-A-Dim".
;;;      Khi thao tac Hatch -> Tu dong chuyen ve "DB-A-Hatch".
;;; ==========================================================================
(vl-load-com);; ===========================================================================
;; PHAN 1: BANG CAU HINH CHUAN LAYER CONG TY (DOI CHIEU CHINH XAC TU 2 ANH MOI)
;; Cau truc: (Ten_Layer  Mau_ACI  Linetype  Lineweight_x100  Plottable_T_or_nil)
;; Ghi chu Lineweight: -3 = Default, 0 = 0.00mm, 9 = 0.09mm, 13 = 0.13mm...
;; ===========================================================================
(defun Get-Company-Layer-Props ()
'(;; --- NHOM ARCHITECTURE (DB-A-...) ---
("DB-A-Area"                7   "Continuous" -3  T)     ; white (7), Default
("DB-A-Area lot long"       6   "Continuous"  0  T)     ; magenta (6), 0.00mm
("DB-A-Area-GFA"            240 "Continuous" -3  nil)   ; 240, Default, No Plot
("DB-A-Area-NSA"            210 "Continuous" -3  nil)   ; 210, Default, No Plot
("DB-A-Code"                53  "Continuous"  0  T)     ; 53, 0.00mm
("DB-A-Dim"                 15  "Continuous"  0  T)     ; 15, 0.00mm
("DB-A-Door"                45  "Continuous"  0  T)     ; 45, 0.00mm
("DB-A-Floor"               167 "Continuous" -3  T)     ; 167, Default
("DB-A-Furniture"           145 "Continuous"  9  T)     ; 145, 0.09mm
("DB-A-Furniture thickline" 145 "Continuous"  9  T)     ; 145, 0.09mm
("DB-A-Furniture thinline"  8   "Continuous"  9  T)     ; 8, 0.09mm
("DB-A-Furniture_"          8   "Continuous"  9  T)     ; 8, 0.09mm
("DB-A-Handrail"            217 "Continuous"  0  T)     ; 217, 0.00mm
("DB-A-Hatch"               252 "Continuous"  0  T)     ; 252, 0.00mm
("DB-A-Hatch Brick"         39  "Continuous" -3  T)     ; 39, Default
("DB-A-Line hidden"         251 "DASHED"      0  nil)   ; 251, DASHED, 0.00mm, No Plot
("DB-A-Line medium"         185 "Continuous" 13  T)     ; 185, 0.13mm
("DB-A-Line strong"         225 "Continuous" 18  T)     ; 225, 0.18mm
("DB-A-Line thin"           8   "Continuous"  9  T)     ; 8, 0.09mm
("DB-A-People"              8   "Continuous" -3  T)     ; 8, Default
("DB-A-Plaster"             39  "Continuous" -3  T)     ; 39, Default
("DB-A-Roof"                61  "Continuous" -3  T)     ; 61, Default
("DB-A-Section line"        181 "DASHED"     40  T)     ; 181, DASHED, 0.40mm
("DB-A-STAIR"               134 "Continuous"  0  T)     ; 134, 0.00mm
("DB-A-Symbol"              111 "Continuous"  0  T)     ; 111, 0.00mm
("DB-A-Text"                163 "Continuous"  0  T)     ; 163, 0.00mm
("DB-A-Unit Type Hatch"     7   "Continuous" -3  T)     ; white (7), Default
("DB-A-Wall"                2   "Continuous" 30  T)     ; yellow (2), 0.30mm
("DB-A-Wall Finished"       46  "Continuous" -3  T)     ; 46, Default
("DB-A-Windows"             43  "Continuous"  0  T)     ; 43, 0.00mm
("DB-Check dim"             8   "Continuous" -3  nil)   ; 8, Default, No Plot;; --- NHOM LANDSCAPE (DB-L-...) ---
("DB-L-Grass"               3   "Continuous"  0  T)     ; green (3), 0.00mm
("DB-L-Hatch Green"         184 "Continuous" -3  T)     ; 184, Default
("DB-L-Landscape Edge"      183 "Continuous" -3  T)     ; 183, Default
("DB-L-Plant"               64  "Continuous"  9  nil)   ; 64, 0.09mm, No Plot
("DB-L-Plot edge"           63  "Continuous" -3  T)     ; 63, Default
("DB-L-Road"                121 "Continuous" -3  T)     ; 121, Default
("DB-L-Road Center"         11  "HIDDEN"     -3  T)     ; 11, HIDDEN, Default
("DB-L-Setback"             5   "PHANTOM"    30  T)     ; blue (5), PHANTOM, 0.30mm
("DB-L-Site Boundary"       1   "PHANTOM"    50  T)     ; red (1), PHANTOM, 0.50mm
("DB-L-Situation"           137 "ACAD_ISO02W100" 0 T)   ; 137, ACAD_ISO02W100, 0.00mm
("DB-L-Timber"              34  "Continuous" -3  T)     ; 34, Default
("DB-L-Tree"                62  "Continuous"  9  nil)   ; 62, 0.09mm, No Plot
("DB-L-Water"               4   "Continuous" -3  T)     ; cyan (4), Default

;; --- NHOM MEP & STRUCTURE (DB-M-... / DB-S-...) ---
("DB-M-Equipment"           37  "Continuous" 13  T)     ; 37, 0.13mm
("DB-MVAC-EQUIPMENT"        65  "Continuous" -3  T)     ; 65, Default
("DB-MVAC-OADUCT"           4   "Continuous" -3  T)     ; cyan (4), Default
("DB-MVAC-SEAD"             1   "Continuous" -3  T)     ; red (1), Default
("DB-MVAC-TEXT"             7   "Continuous" -3  T)     ; white (7), Default
("DB-S-Beam"                30  "Continuous" 40  T)     ; 30, 0.40mm
("DB-S-Column"              20  "Continuous" 40  T)     ; 20, 0.40mm
("DB-S-Hatch Concrete"      250 "Continuous" -3  T)     ; 250, Default
("DB-S-Slab"                9   "Continuous"  0  T)     ; 9, 0.00mm
("DB-S-Wall"                20  "Continuous" 40  T)     ; 20, 0.40mm
)
);; Ham tra cuu ten chuan xac (Hoa/Thuong) trong danh sach Layer cong ty
(defun Get-Exact-Company-Name (nameStr / u res)
(setq u (strcase nameStr))
(foreach item (Get-Company-Layer-Props)
(if (= (strcase (car item)) u)
(setq res (car item))
)
)
res
);; Ham kiem tra Layer thuoc nhom BAO TOAN TUYET DOI (Hatch & NSA, GSA, GBA, GFA, CFA, Area)
;; -> Khong doi ten, khong dich, khong ep mau/thuoc tinh, khong xoa
(defun DB-Is-Protected-Layer (layName / u p)
(setq u (strcase layName))
(while (setq p (vl-string-search "$0$" u))
(setq u (substr u (+ p 4)))
)
(wcmatch u "HATCH,TO VACH,TO_VACH,PAT,BATTING,NSA,GSA,GBA,GFA,CFA,AREA,LOT LONG,LOT_LONG,THONG THUY,THONG_THUY,THONG-THUY,DIEN TICH,DIEN_TICH,DIEN-TICH,DIENTICH")
);; Ham khoi tao Layer cong ty:
;; CHI TAO MOI KHI CHUA CO TRONG BAN VE.
;; NEU DA CO SAN TRONG BAN VE -> GIU NGUYEN 100% MAU SAC, LINETYPE, LINEWEIGHT HIEN TAI!
(defun Ensure-Company-Layer (layName / doc layers ltypes exact props col ltype lw plot layObj)
(setq doc    (vla-get-ActiveDocument (vlax-get-acad-object))
layers (vla-get-Layers doc)
ltypes (vla-get-Linetypes doc)
exact  (Get-Exact-Company-Name layName)
)
(if exact (setq layName exact));; Chi khi Layer CHUA TON TAI trong ban ve thi moi tao moi va gan thong so chuan
(if (not (tblsearch "LAYER" layName))
(progn
(setq layObj (vla-Add layers layName)
props  (cdr (assoc layName (Get-Company-Layer-Props)))
)
(if props
(progn
(setq col   (nth 0 props)
ltype (nth 1 props)
lw    (nth 2 props)
plot  (nth 3 props)
)
(vl-catch-all-apply 'vla-put-Color (list layObj col))
(if (not (equal (strcase ltype) "CONTINUOUS"))
(progn
(if (not (tblobjname "LTYPE" ltype))
(vl-catch-all-apply 'vla-Load (list ltypes ltype "acad.lin"))
)
(if (tblobjname "LTYPE" ltype)
(vl-catch-all-apply 'vla-put-Linetype (list layObj ltype))
)
)
(vl-catch-all-apply 'vla-put-Linetype (list layObj "Continuous"))
)
(vl-catch-all-apply 'vla-put-Lineweight (list layObj lw))
(vl-catch-all-apply 'vla-put-Plottable (list layObj (if plot :vlax-true :vlax-false)))
)
)
)
;; Neu Layer DA TON TAI (co the khac hoa/thuong) -> Lay dung ten hien tai trong ban ve, KHONG doi mau
(setq layName (cdr (assoc 2 (tblsearch "LAYER" layName))))
)
layName
);; ===========================================================================
;; PHAN 2: TU DONG CHUYEN LAYER KHI DUNG LENH DIM & HATCH (REACTOR + SYSVAR)
;; ===========================================================================
(defun Setup-Auto-Dim-Hatch-Layer ()
(Ensure-Company-Layer "DB-A-Dim")
(Ensure-Company-Layer "DB-A-Hatch")
(if (getvar "DIMLAYER") (vl-catch-all-apply 'setvar (list "DIMLAYER" "DB-A-Dim")))
(if (getvar "HPLAYER")  (vl-catch-all-apply 'setvar (list "HPLAYER"  "DB-A-Hatch")))(if (not DB-Layer-Reactor)
(setq DB-Layer-Reactor
(vlr-command-reactor nil
'((:vlr-commandWillStart . DB-Command-WillStart)
(:vlr-commandEnded     . DB-Command-Ended)
(:vlr-commandCancelled . DB-Command-Ended)
(:vlr-commandFailed    . DB-Command-Ended))
)
)
)
)(defun DB-Command-WillStart (reactor params / cmd)
(setq cmd (strcase (car params)))
(cond
;; Khi bat dau lenh Dim hoac Leader -> Chuyen sang DB-A-Dim
((wcmatch cmd "DIM*,DLI,DAL,DAR,DJOG,DOR,DDI,DRA,DAN,DBA,DCO,QDIM,LEADER,QLEADER,MLEADER")
(setq DB-Old-Clayer (getvar "CLAYER"))
(Ensure-Company-Layer "DB-A-Dim")
(setvar "CLAYER" "DB-A-Dim")
)
;; Khi bat dau lenh Hatch / Bhatch / Gradient -> Chuyen sang DB-A-Hatch
((wcmatch cmd "HATCH,GRADIENT")
(setq DB-Old-Clayer (getvar "CLAYER"))
(Ensure-Company-Layer "DB-A-Hatch")
(setvar "CLAYER" "DB-A-Hatch")
)
)
)(defun DB-Command-Ended (reactor params / cmd)
(setq cmd (strcase (car params)))
(if (and DB-Old-Clayer
(wcmatch cmd "DIM*,DLI,DAL,DAR,DJOG,DOR,DDI,DRA,DAN,DBA,DCO,QDIM,LEADER,QLEADER,MLEADER,HATCH,GRADIENT"))
(progn
(if (tblsearch "LAYER" DB-Old-Clayer)
(vl-catch-all-apply 'setvar (list "CLAYER" DB-Old-Clayer))
)
(setq DB-Old-Clayer nil)
)
)
);; Kich hoat ngay khi load Lisp
(Setup-Auto-Dim-Hatch-Layer);; ===========================================================================
;; PHAN 3: LENH TLAY / FIXLAY THEO VUNG CHON (GIU NGUYEN LAYER DB- & HATCH/AREA)
;; ===========================================================================
(defun c:TLAY ( / doc layers blocks ss i ent vlaEnt touchedLayers processedBlocks countObj countLay)
(setq doc             (vla-get-ActiveDocument (vlax-get-acad-object))
layers          (vla-get-Layers doc)
blocks          (vla-get-Blocks doc)
touchedLayers   '()
processedBlocks '()
countObj        0
countLay        0
);; Bo tu dien tich hop FIXLAY ($0$) va TLAY
(defun Translate-Layer-Name (rawName / clean u p hasPrefix exactName)
(cond
;; 1. Neu la Layer Hatch hoac NSA, GSA, GBA, GFA, CFA, Area -> GIU NGUYEN 100%, khong dung cham
((DB-Is-Protected-Layer rawName)
nil
)
;; 2. Neu la Layer bat dau bang "DB-" (va khong dinh tien to Xref $0$) -> GIU NGUYEN 100%
((and (wcmatch (strcase rawName) "DB-*")
(not (vl-string-search "$0$" rawName)))
nil
)
;; 3. Cac truong hop con lai (Layer dinh $0$ hoac Layer ngoai lai)
(T
(setq clean     rawName
hasPrefix nil
)
;; Cat bo toan bo tien to Xref Bind "...$0$"
(while (setq p (vl-string-search "$0$" clean))
(setq clean     (substr clean (+ p 4))
hasPrefix T
)
)
(setq u (strcase clean))    ;; Chuan hoa cac ten he A_ cu hoac go lech
    (cond
      ((= u "DB-A-WINDOW")  (setq clean "DB-A-Windows"   u (strcase clean)))
      ((= u "A_DOOR")       (setq clean "DB-A-Door"      u (strcase clean)))
      ((= u "A_WINDOW")     (setq clean "DB-A-Windows"   u (strcase clean)))
      ((= u "A_WALL")       (setq clean "DB-A-Wall"      u (strcase clean)))
      ((= u "A_FURNITURE")  (setq clean "DB-A-Furniture" u (strcase clean)))
      ((= u "A_KITCHEN")    (setq clean "DB-A-Furniture" u (strcase clean)))
    )

    (cond
      ;; Neu sau khi cat $0$ la Layer DB- cua cong ty -> Gop ve Layer DB- do
      ((wcmatch u "DB-*")
        (setq exactName (Get-Exact-Company-Name clean))
        (if exactName exactName clean)
      )
      ;; Neu sau khi cat $0$ la Layer "0" hoac "Defpoints"
      ((and hasPrefix (or (= u "0") (= u "DEFPOINTS")))
        clean
      )
      ;; Dich tu khoa Tieng Viet + Tieng Anh sang Layer chuan cong ty
      ;; (Tuyet doi khong dich Hatch va NSA/GSA/GBA/GFA/CFA/Area)
      (T
        (cond
          ;; --- NHOM KET CAU (STRUCTURE: DB-S-...) ---
          ((wcmatch u "*COT*,*COLUMN*,*COL_*,*COL-*,*KC-COT*,*KC_COT*,*S-COL*,*S_COL*,*PILLAR*,*TRU *") "DB-S-Column")
          ((wcmatch u "*DAM*,*BEAM*,*KC-DAM*,*KC_DAM*,*S-BEAM*,*S_BEAM*,*LANH TO*,*LINHTO*") "DB-S-Beam")
          ((wcmatch u "*SLAB*,*SAN KC*,*SAN_KC*,*SAN BT*,*SAN_BT*,*S-SLAB*,*S_SLAB*,*KC-SAN*,*KC_SAN*") "DB-S-Slab")
          ((wcmatch u "*VACH BT*,*VACH_BT*,*TUONG VACH*,*R.C WALL*,*RC WALL*,*SHEAR WALL*,*S-WALL*,*S_WALL*,*KC-VACH*,*KC_VACH*") "DB-S-Wall")

          ;; --- NHOM TUONG & HOAN THIEN (ARCHITECTURE) ---
          ((wcmatch u "*PLASTER*,*TRAT*,*VUA TRAT*") "DB-A-Plaster")
          ((wcmatch u "*TUONG HT*,*TUONG_HT*,*HOAN THIEN*,*WALL FINISH*,*OP TUONG*,*WALL TILE*,*SKIRTING*,*LEN CHAN*") "DB-A-Wall Finished")
          ((wcmatch u "*TUONG*,*TƯỜNG*,*WALL*,*VACH*,*PARTITION*") "DB-A-Wall")

          ;; --- NHOM CUA DI & CUA SO ---
          ((wcmatch u "*CUA SO*,*CUA_SO*,*CUA-SO*,*CUASO*,*CỬA SỔ*,*WINDOW*,*WIN*,*VACH KINH*,*GLASS*,*LOUVRE*,*LAM *") "DB-A-Windows")
          ((wcmatch u "*CUA*,*CỬA*,*DOOR*") "DB-A-Door")

          ;; --- NHOM THANG & LAN CAN ---
          ((wcmatch u "*LAN CAN*,*LAN_CAN*,*LANCAN*,*TAY VIN*,*TAY_VIN*,*HANDRAIL*,*RAILING*,*RAILLING*,*BALUSTRADE*,*PARAPET*") "DB-A-Handrail")
          ((wcmatch u "*THANG*,*STAIR*,*BAC THANG*,*STEP*,*RAMP*,*DOC *,*ESCALATOR*,*LIFT*,*ELEVATOR*") "DB-A-STAIR")

          ;; --- NHOM SAN & MAI ---
          ((wcmatch u "*MAI*,*MÁI*,*ROOF*,*CANOPY*,*SENO*,*MANG XOI*") "DB-A-Roof")
          ((wcmatch u "*SAN*,*SÀN*,*FLOOR*,*NEN*,*NỀN*,*LAT GACH*,*TILE*,*BALCONY*,*BAN CONG*") "DB-A-Floor")

          ;; --- NHOM NOI THAT, CON NGUOI & THIET BI ---
          ((wcmatch u "*PEOPLE*,*NGUOI*") "DB-A-People")
          ((wcmatch u "*OADUCT*") "DB-MVAC-OADUCT")
          ((wcmatch u "*SEAD*") "DB-MVAC-SEAD")
          ((wcmatch u "*MVAC*TEXT*,*HVAC*TEXT*") "DB-MVAC-TEXT")
          ((wcmatch u "*MVAC*,*HVAC*,*DIEU HOA*") "DB-MVAC-EQUIPMENT")
          ((wcmatch u "*M-EQUIP*,*MEP*") "DB-M-Equipment")
          ((wcmatch u "*NOI THAT*,*NOI_THAT*,*NOITHAT*,*NỘI THẤT*,*FURNITURE*,*FURN*,*LOOSE*,*BAN GHE*,*GIUONG*,*TU AO*,*WARDROBE*,*KITCHEN*,*BEP*,*SANITARY*,*TBVS*,*VE SINH*,*TOILET*,*WC*,*FIXTURE*,*EQUIPMENT*,*THIET BI*") "DB-A-Furniture")

          ;; --- NHOM KICH THUOC, CHU, KY HIEU ---
          ((wcmatch u "*DIM*,*KICH THUOC*,*KICH_THUOC*,*KICHTHUOC*,*KÍCH THƯỚC*") "DB-A-Dim")
          ((wcmatch u "*CODE*,*MA HIEU*,*MA_HIEU*,*TAG*") "DB-A-Code")
          ((wcmatch u "*TEXT*,*CHU*,*CHỮ*,*GHI CHU*,*GHI_CHU*,*GHICHU*,*NOTE*,*TITLE*,*TIEU DE*,*ROOM NAME*,*TEN PHONG*") "DB-A-Text")
          ((wcmatch u "*SECTION LINE*,*NET CAT*,*NET_CAT*,*DUONG CAT*,*MAT CAT*") "DB-A-Section line")
          ((wcmatch u "*SYMBOL*,*KY HIEU*,*KY_HIEU*,*KÝ HIỆU*,*CAO DO*,*LEVEL*,*ARROW*,*MUI TEN*,*CLOUD*,*GRID*,*TRUC*,*TIM TRUC*") "DB-A-Symbol")

          ;; --- NHOM CANH QUAN & HA TANG (LANDSCAPE: DB-L-...) ---
          ((wcmatch u "*RANH DAT*,*RANH_DAT*,*RANHDAT*,*RANH GIOI*,*SITE BOUNDARY*,*BOUNDARY*,*RED LINE*,*CHỈ GIỚI*") "DB-L-Site Boundary")
          ((wcmatch u "*SETBACK*,*KHOANG LUI*,*KHOANG_LUI*,*CHỈ GIỚI XÂY DỰNG*,*CGXD*") "DB-L-Setback")
          ((wcmatch u "*PLOT EDGE*,*RANH LO*,*RANH_LO*,*PHAN LO*") "DB-L-Plot edge")
          ((wcmatch u "*SITUATION*,*HIEN TRANG*,*HIEN_TRANG*,*HIENTRANG*") "DB-L-Situation")
          ((wcmatch u "*TIMBER*,*SAN GO*,*GO NGOAI TROI*,*DECK*") "DB-L-Timber")
          ((wcmatch u "*GRASS*,*THAM CO*,*THAM_CO*,*CO *,*CỎ*,*LAWN*") "DB-L-Grass")
          ((wcmatch u "*TREE*,*CAY XANH*,*CAY_XANH*,*CÂY*,*CAY BONG MAT*") "DB-L-Tree")
          ((wcmatch u "*PLANT*,*CAY BUI*,*CAY_BUI*,*BON CAY*,*HOA *,*SHRUB*") "DB-L-Plant")
          ((wcmatch u "*LANDSCAPE*,*CANH QUAN*,*CANH_QUAN*,*SAN VUON*,*WALKWAY*,*LOI DI*,*VIA HE*,*KERB*,*BO VIA*") "DB-L-Landscape Edge")
          ((wcmatch u "*WATER*,*MAT NUOC*,*MAT_NUOC*,*HO BOI*,*BE BOI*,*POOL*,*LAKE*,*POND*") "DB-L-Water")
          ((wcmatch u "*ROAD CENTER*,*CENTRE OF ROAD*,*TIM DUONG*,*TIM_DUONG*") "DB-L-Road Center")
          ((wcmatch u "*ROAD*,*DUONG GIAO THONG*,*DUONG_GT*,*LONG DUONG*,*CAR*,*PARKING*,*BAI XE*") "DB-L-Road")

          ;; --- NHOM NET VE CHUNG (LINE HIDDEN, THIN, MEDIUM, STRONG) ---
          ((wcmatch u "*HIDDEN*,*NET DUT*,*NET_DUT*,*NET KHUAT*,*NET_KHUAT*,*OPENING*,*LO MO*") "DB-A-Line hidden")
          ((wcmatch u "*STRONG*,*NET DAM*,*NET_DAM*,*THICK*") "DB-A-Line strong")
          ((wcmatch u "*MEDIUM*,*NET VUA*,*NET_VUA*,*NET TRUNG*,*NET THAY*") "DB-A-Line medium")
          ((wcmatch u "*THIN*,*NET MANH*,*NET_MANH*,*MANH*,*MẢNH*,*FINE*") "DB-A-Line thin")

          (hasPrefix clean)
          (T nil)
        )
      )
    )
  )
)
);; Ham xu ly 1 doi tuong VLA (va de quy chui vao trong Block neu doi tuong duoc chon la Block)
(defun Process-Vla-Entity (obj / objType oldName targetName bName blkDef subEnt att)
(setq objType (vla-get-ObjectName obj)
oldName (vla-get-Layer obj)
)
;; 1. Xu ly ban than doi tuong (Bo qua 100% AcDbHatch va Layer Hatch/NSA/GSA/GBA/GFA/CFA/Area)
(if (and (/= objType "AcDbHatch")
(not (DB-Is-Protected-Layer oldName)))
(progn
(setq targetName (Translate-Layer-Name oldName))
(if targetName
(progn
(if (not (member (strcase oldName) touchedLayers))
(setq touchedLayers (cons (strcase oldName) touchedLayers))
)
(setq targetName (Ensure-Company-Layer targetName))
(if (not (vl-catch-all-error-p (vl-catch-all-apply 'vla-put-Layer (list obj targetName))))
(progn
(vl-catch-all-apply 'vla-put-Color (list obj 256))     ; ByLayer
(vl-catch-all-apply 'vla-put-Linetype (list obj "ByLayer"))
(vl-catch-all-apply 'vla-put-LineWeight (list obj -1)) ; ByLayer
(setq countObj (1+ countObj))
)
)
)
)
)
);; 2. Neu doi tuong duoc chon la Block Reference -> Xu ly Attribute & cac doi tuong ben trong Block do
(if (= objType "AcDbBlockReference")
  (progn
    ;; Xu ly cac Attribute dinh kem voi Block Reference nay
    (if (= (vla-get-HasAttributes obj) :vlax-true)
      (foreach att (vlax-invoke obj 'GetAttributes)
        (Process-Vla-Entity att)
      )
    )
    ;; Lay ten Block Definition (ho tro ca Dynamic Block)
    (setq bName (if (vlax-property-available-p obj 'EffectiveName)
                  (vla-get-EffectiveName obj)
                  (vla-get-Name obj)
                )
    )
    ;; Chi quet cac Block nam trong vung chon va khong quet trung lap
    (if (and (not (member (strcase bName) processedBlocks))
             (not (wcmatch bName "*|*")))
      (progn
        (setq processedBlocks (cons (strcase bName) processedBlocks))
        (if (not (vl-catch-all-error-p (setq blkDef (vl-catch-all-apply 'vla-Item (list blocks bName)))))
          (if (= (vla-get-IsXRef blkDef) :vlax-false)
            (vlax-for subEnt blkDef
              (Process-Vla-Entity subEnt)
            )
          )
        )
      )
    )
  )
)
);; YEU CAU NGUOI DUNG QUET CHON VUNG DOI TUONG CAN CHUAN HOA LAYER
(princ "\n>>> [TLAY] Quet chon vung doi tuong can dich & chuan hoa Layer (Giữ nguyên Hatch & NSA/GSA/GBA/GFA/CFA): ")
(if (setq ss (ssget))
(progn
(vla-StartUndoMark doc)  ;; 1. Duyet qua tung doi tuong trong vung nguoi dung quet chon
  (setq i 0)
  (while (< i (sslength ss))
    (setq ent    (ssname ss i)
          vlaEnt (vlax-ename->vla-object ent)
    )
    (Process-Vla-Entity vlaEnt)
    (setq i (1+ i))
  )

  ;; 2. Don sach (Purge) cac Layer ngoai lai / $0$ VUA DUOC CHUYEN neu chung da hoan toan trong
  (if (member (strcase (getvar "CLAYER")) touchedLayers)
    (setvar "CLAYER" "0")
  )

  (vlax-for lay layers
    (setq oldName (vla-get-Name lay))
    (if (member (strcase oldName) touchedLayers)
      (if (not (vl-catch-all-error-p (vl-catch-all-apply 'vla-Delete (list lay))))
        (setq countLay (1+ countLay))
      )
    )
  )

  ;; Kich hoat lai che do Auto Dim / Hatch Layer
  (Setup-Auto-Dim-Hatch-Layer)

  (vla-EndUndoMark doc)
  ;; Chi Regen khi co xu ly ben trong Block de khong bi lag ban ve
  (if processedBlocks
    (vla-Regen doc acActiveViewport)
  )

  (princ (strcat "\n>>> [TLAY HOAN TAT] Da chuyen "
                 (itoa countObj) " doi tuong trong vung chon & xoa "
                 (itoa countLay) " Layer ngoai lai da trong!"))
)
(princ "\n>>> Da huy lenh TLAY (Khong co doi tuong nao duoc chon).")
)
(princ)
)(defun c:FIXLAY () (c:TLAY))(princ "\n*** Go TLAY (hoac FIXLAY) -> Quet chon vung doi tuong de chuan hoa Layer ***")
(princ)