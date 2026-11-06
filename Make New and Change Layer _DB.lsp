;;; ==========================================================================
;;; FILE: Make New and Change Layer _DB.lsp
;;; - Bo lenh A1 -> A38 tao moi & chuyen Layer chuan cong ty (AutoCAD & ZWCAD)
;;; - Da kiem tra va dong bo 100% Mau sac, Linetype, Lineweight, Plot theo 2 anh
;;; - Tich hop Auto Layer: Khi dung lenh DIM tu dong chuyen ve "DB-A-Dim",
;;;   khi dung lenh HATCH tu dong chuyen ve "DB-A-Hatch"
;;; ==========================================================================
(vl-load-com)

;;; ==========================================================================
;;; 1. HAM PHU TRO TAO / CAP NHAT VA CHUYEN DOI TUONG SANG LAYER CONG TY
;;;    Tham so:
;;;    - layName  : Ten Layer chuan cong ty
;;;    - layColor : Ma mau ACI (1->255)
;;;    - layLtype : Kieu duong net ("Continuous", "DASHED", "HIDDEN", "PHANTOM"...)
;;;    - layLw    : Do day net (-3 = Default, 0 = 0.00mm, 9 = 0.09mm, 30 = 0.30mm...)
;;;    - isPlot   : T = Co in (Plot), nil = Tat in (No Plot)
;;; ==========================================================================
(defun EnsureCompanyLayerProps (layName layColor layLtype layLw isPlot / doc layers ltypes layObj)
(setq doc    (vla-get-ActiveDocument (vlax-get-acad-object))
layers (vla-get-Layers doc)
ltypes (vla-get-Linetypes doc)
)
;; Neu chua co Layer thi tao moi va gan day du thuoc tinh chuan
;; Neu da co Layer nhung bi sai mau/net so voi chuan anh chup thi dong bo lai cho dung
(if (not (tblsearch "LAYER" layName))
(setq layObj (vla-Add layers layName))
(setq layObj (vla-Item layers layName))
)
(vl-catch-all-apply 'vla-put-Color (list layObj layColor))

;; Nap va gan Linetype
(if (and layLtype (not (equal (strcase layLtype) "CONTINUOUS")))
(progn
(if (not (tblobjname "LTYPE" layLtype))
(vl-catch-all-apply 'vla-Load (list ltypes layLtype "acad.lin"))
)
(if (tblobjname "LTYPE" layLtype)
(vl-catch-all-apply 'vla-put-Linetype (list layObj layLtype))
)
)
(vl-catch-all-apply 'vla-put-Linetype (list layObj "Continuous"))
)

;; Gan Lineweight (-3 la Default, cac so khac tinh theo 1/100 mm)
(if layLw
(vl-catch-all-apply 'vla-put-Lineweight (list layObj layLw))
)

;; Gan trang thai In (Plot / No Plot)
(vl-catch-all-apply 'vla-put-Plottable (list layObj (if isPlot :vlax-true :vlax-false)))
layObj
)

(defun ChangeToCompanyLayer (layName layColor layLtype layLw isPlot / ss)
(vl-catch-all-apply
'(lambda ()
;; 1. Dam bao Layer ton tai va mang dung Mau sac + Linetype + Lineweight + Plot
(EnsureCompanyLayerProps layName layColor layLtype layLw isPlot)

   ;; 2. Neu dang chon doi tuong hoac quet chon doi tuong -> Chuyen sang Layer & ByLayer
   ;;    Dong thoi dat Layer do lam Layer hien hanh (Current Layer)
   (setvar "CLAYER" layName)
   (if (setq ss (ssget))
     (command "_.chprop" ss "" "_LA" layName "_C" "_BYLAYER" "_LT" "_BYLAYER" "_LW" "_BYLAYER" "")
   )
 )


)
(princ (strcat "\n>> Da chuyen sang layer: " layName " [Color: " (itoa layColor) " | Ltype: " layLtype "]"))
(princ)
)

;;; ==========================================================================
;;; 2. TU DONG CHUYEN LAYER KHI THAO TAC LENH DIM & LENH HATCH
;;;    - Khi go bat ky lenh Dim nao -> Tu dong dua ve layer "DB-A-Dim" (Mau 15)
;;;    - Khi go bat ky lenh Hatch nao -> Tu dong dua ve layer "DB-A-Hatch" (Mau 252)
;;; ==========================================================================
(defun SetupAutoDimHatchLayer ()
;; Khoi tao san 2 layer DB-A-Dim va DB-A-Hatch theo dung thong so tren anh
(EnsureCompanyLayerProps "DB-A-Dim"   15  "Continuous" 0 T)
(EnsureCompanyLayerProps "DB-A-Hatch" 252 "Continuous" 0 T)

;; Gan bien he thong DIMLAYER va HPLAYER (ho tro truc tiep tren AutoCAD & ZWCAD)
(if (getvar "DIMLAYER") (vl-catch-all-apply 'setvar (list "DIMLAYER" "DB-A-Dim")))
(if (getvar "HPLAYER")  (vl-catch-all-apply 'setvar (list "HPLAYER"  "DB-A-Hatch")))

;; Cai dat Reactor bat su kien khi bat dau va ket thuc lenh Dim / Hatch
(if (not DB-Layer-Reactor)
(setq DB-Layer-Reactor
(vlr-command-reactor nil
'((:vlr-commandWillStart . DB-Command-WillStart)
(:vlr-commandEnded     . DB-Command-Ended)
(:vlr-commandCancelled . DB-Command-Ended)
(:vlr-commandFailed    . DB-Command-Ended))
)
)
)
)

(defun DB-Command-WillStart (reactor params / cmd)
(setq cmd (strcase (car params)))
(cond
;; Bat tat ca cac lenh ghi kich thuoc (Dim) va mui ten ghi chu (Leader)
((wcmatch cmd "DIM*,DLI,DAL,DAR,DJOG,DOR,DDI,DRA,DAN,DBA,DCO,QDIM,LEADER,QLEADER,MLEADER")
(setq DB-Old-Clayer (getvar "CLAYER"))
(EnsureCompanyLayerProps "DB-A-Dim" 15 "Continuous" 0 T)
(setvar "CLAYER" "DB-A-Dim")
)
;; Bat tat ca cac lenh to vat lieu (Hatch / Bhatch / Gradient)
((wcmatch cmd "HATCH,GRADIENT")
(setq DB-Old-Clayer (getvar "CLAYER"))
(EnsureCompanyLayerProps "DB-A-Hatch" 252 "Continuous" 0 T)
(setvar "CLAYER" "DB-A-Hatch")
)
)
)

(defun DB-Command-Ended (reactor params / cmd)
(setq cmd (strcase (car params)))
(if (and DB-Old-Clayer
(wcmatch cmd "DIM*,DLI,DAL,DAR,DJOG,DOR,DDI,DRA,DAN,DBA,DCO,QDIM,LEADER,QLEADER,MLEADER,HATCH,GRADIENT"))
(progn
;; Tra lai Layer hien hanh truoc do sau khi Dim/Hatch xong
(if (tblsearch "LAYER" DB-Old-Clayer)
(vl-catch-all-apply 'setvar (list "CLAYER" DB-Old-Clayer))
)
(setq DB-Old-Clayer nil)
)
)
)

;; Kich hoat ngay khi Load file Lisp
(SetupAutoDimHatchLayer)

;;; ==========================================================================
;;; 3. NHOM ARCHITECTURE (DB-A-...) - DOI CHIEU CHINH XAC TU ANH 1
;;;    Cau truc: (ChangeToCompanyLayer Ten_Layer Mau Linetype Lineweight Plot)
;;;    Lineweight: -3 = Default, 0 = 0.00mm, 9 = 0.09mm, 13 = 0.13mm, 18 = 0.18mm,
;;;                30 = 0.30mm, 40 = 0.40mm, 50 = 0.50mm
;;; ==========================================================================
(defun c:A1 ()  (ChangeToCompanyLayer "DB-A-Wall"            2   "Continuous" 30 T))   ; yellow, 0.30mm
(defun c:A2 ()  (ChangeToCompanyLayer "DB-A-Wall Finished"   46  "Continuous" -3 T))   ; color 46, Default
(defun c:A3 ()  (ChangeToCompanyLayer "DB-A-Hatch"           252 "Continuous"  0 T))   ; color 252, 0.00mm
(defun c:A4 ()  (ChangeToCompanyLayer "DB-A-Floor"           167 "Continuous" -3 T))   ; color 167, Default
(defun c:A5 ()  (ChangeToCompanyLayer "DB-A-Roof"            61  "Continuous" -3 T))   ; color 61, Default
(defun c:A6 ()  (ChangeToCompanyLayer "DB-A-Door"            45  "Continuous"  0 T))   ; color 45, 0.00mm
(defun c:A7 ()  (ChangeToCompanyLayer "DB-A-Windows"         43  "Continuous"  0 T))   ; color 43, 0.00mm (co chu s)
(defun c:A8 ()  (ChangeToCompanyLayer "DB-A-STAIR"           134 "Continuous"  0 T))   ; color 134, 0.00mm
(defun c:A9 ()  (ChangeToCompanyLayer "DB-A-Handrail"        217 "Continuous"  0 T))   ; color 217, 0.00mm
(defun c:A10 () (ChangeToCompanyLayer "DB-A-Furniture"       145 "Continuous"  9 T))   ; color 145, 0.09mm
(defun c:A11 () (ChangeToCompanyLayer "DB-A-Dim"             15  "Continuous"  0 T))   ; color 15, 0.00mm
(defun c:A12 () (ChangeToCompanyLayer "DB-A-Line strong"     225 "Continuous" 18 T))   ; color 225, 0.18mm
(defun c:A13 () (ChangeToCompanyLayer "DB-A-Line medium"     185 "Continuous" 13 T))   ; color 185, 0.13mm
(defun c:A14 () (ChangeToCompanyLayer "DB-A-Line thin"       8   "Continuous"  9 T))   ; color 8, 0.09mm
(defun c:A15 () (ChangeToCompanyLayer "DB-A-Line hidden"     251 "DASHED"      0 nil)) ; color 251, DASHED, 0.00mm, No Plot
(defun c:A16 () (ChangeToCompanyLayer "DB-A-Text"            163 "Continuous"  0 T))   ; color 163, 0.00mm
(defun c:A17 () (ChangeToCompanyLayer "DB-A-Code"            53  "Continuous"  0 T))   ; color 53, 0.00mm
(defun c:A18 () (ChangeToCompanyLayer "DB-A-Symbol"          111 "Continuous"  0 T))   ; color 111, 0.00mm
(defun c:A19 () (ChangeToCompanyLayer "DB-A-Section line"    181 "DASHED"     40 T))   ; color 181, DASHED, 0.40mm
(defun c:A20 () (ChangeToCompanyLayer "DB-A-Area"            7   "Continuous" -3 T))   ; white (7), Default

;;; ==========================================================================
;;; 4. NHOM LANDSCAPE (DB-L-...) - DOI CHIEU CHINH XAC TU ANH 2
;;; ==========================================================================
(defun c:A21 () (ChangeToCompanyLayer "DB-L-Site Boundary"   1   "PHANTOM"    50 T))   ; red (1), PHANTOM, 0.50mm
(defun c:A22 () (ChangeToCompanyLayer "DB-L-Setback"         5   "PHANTOM"    30 T))   ; blue (5), PHANTOM, 0.30mm
(defun c:A23 () (ChangeToCompanyLayer "DB-L-Situation"       137 "ACAD_ISO02W100" 0 T)); color 137, ACAD_ISO02W100, 0.00mm
(defun c:A24 () (ChangeToCompanyLayer "DB-L-Grass"           3   "Continuous"  0 T))   ; green (3), 0.00mm
(defun c:A25 () (ChangeToCompanyLayer "DB-L-Tree"            62  "Continuous"  9 nil)) ; color 62, 0.09mm, No Plot
(defun c:A26 () (ChangeToCompanyLayer "DB-L-Plant"           64  "Continuous"  9 nil)) ; color 64, 0.09mm, No Plot
(defun c:A27 () (ChangeToCompanyLayer "DB-L-Landscape Edge"  183 "Continuous" -3 T))   ; color 183, Default
(defun c:A28 () (ChangeToCompanyLayer "DB-L-Water"           4   "Continuous" -3 T))   ; cyan (4), Default
(defun c:A29 () (ChangeToCompanyLayer "DB-L-Road"            121 "Continuous" -3 T))   ; color 121, Default
(defun c:A30 () (ChangeToCompanyLayer "DB-L-Road Center"     11  "HIDDEN"     -3 T))   ; color 11, HIDDEN, Default
(defun c:A31 () (ChangeToCompanyLayer "DB-L-Plot edge"       63  "Continuous" -3 T))   ; color 63, Default

;;; ==========================================================================
;;; 5. NHOM STRUCTURE (DB-S-...) - DOI CHIEU CHINH XAC TU ANH 2
;;; ==========================================================================
(defun c:A32 () (ChangeToCompanyLayer "DB-S-Wall"            20  "Continuous" 40 T))   ; color 20, 0.40mm
(defun c:A33 () (ChangeToCompanyLayer "DB-S-Slab"            9   "Continuous"  0 T))   ; color 9, 0.00mm
(defun c:A34 () (ChangeToCompanyLayer "DB-S-Column"          20  "Continuous" 40 T))   ; color 20, 0.40mm
(defun c:A35 () (ChangeToCompanyLayer "DB-S-Beam"            30  "Continuous" 40 T))   ; color 30, 0.40mm

;;; ==========================================================================
;;; 6. NHOM ARCHITECTURE AREA & BO SUNG (A36 -> A48 THEO ANH 1 & ANH 2)
;;; ==========================================================================
(defun c:A36 () (ChangeToCompanyLayer "DB-A-Area"            7   "Continuous" -3 T))   ; white (7), Default
(defun c:A37 () (ChangeToCompanyLayer "DB-A-Area-GFA"        240 "Continuous" -3 nil)) ; color 240, Default, No Plot
(defun c:A38 () (ChangeToCompanyLayer "DB-A-Area-NSA"        210 "Continuous" -3 nil)) ; color 210, Default, No Plot

;; Bo sung them cac Layer co trong 2 anh nhung chua co trong A1-A38 cu (tu A39 -> A49):
(defun c:A39 () (ChangeToCompanyLayer "DB-A-Area lot long"       6   "Continuous"  0 T)) ; magenta (6), 0.00mm
(defun c:A40 () (ChangeToCompanyLayer "DB-A-Furniture thickline" 145 "Continuous"  9 T)) ; color 145, 0.09mm
(defun c:A41 () (ChangeToCompanyLayer "DB-A-Furniture thinline"  8   "Continuous"  9 T)) ; color 8, 0.09mm
(defun c:A42 () (ChangeToCompanyLayer "DB-A-Hatch Brick"         39  "Continuous" -3 T)) ; color 39, Default
(defun c:A43 () (ChangeToCompanyLayer "DB-A-People"              8   "Continuous" -3 T)) ; color 8, Default
(defun c:A44 () (ChangeToCompanyLayer "DB-A-Plaster"             39  "Continuous" -3 T)) ; color 39, Default
(defun c:A45 () (ChangeToCompanyLayer "DB-A-Unit Type Hatch"     7   "Continuous" -3 T)) ; white (7), Default
(defun c:A46 () (ChangeToCompanyLayer "DB-Check dim"             8   "Continuous" -3 nil)) ; color 8, Default, No Plot
(defun c:A47 () (ChangeToCompanyLayer "DB-L-Hatch Green"         18  "Continuous" -3 T)) ; color 18, Default
(defun c:A48 () (ChangeToCompanyLayer "DB-L-Timber"              34  "Continuous" -3 T)) ; color 34, Default
(defun c:A49 () (ChangeToCompanyLayer "DB-S-Hatch Concrete"      250 "Continuous" -3 T)) ; color 250, Default
(defun c:A50 () (ChangeToCompanyLayer "DB-M-Equipment"           37  "Continuous" 13 T)) ; color 37, 0.13mm
(defun c:A51 () (ChangeToCompanyLayer "DB-MVAC-EQUIPMENT"        65  "Continuous" -3 T)) ; color 65, Default

(princ "\n*** Da load Make New and Change Layer _DB (A1 -> A51) | Tu dong nhay Layer DB-A-Dim & DB-A-Hatch ***")
(princ)