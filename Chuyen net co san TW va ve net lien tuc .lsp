(vl-load-com)

;; ==========================================================================
;; HAM PHU TRO: XU LY TAO TUONG, HATCH VA CHUYEN TIM SANG LAYER AN
;; ==========================================================================
(defun MakeWallFromCenter (obj w side doc space / d1 d2 c1 c2 a1 a2 pt1 pt2 pt3 pt4 l1 l2 e1 e2 el1 el2 bndEnt bndObj hatchObj loopObj loop1 loop2 safe-offset isClosed)

  ;; Ham offset an toan cho Lisp
  (defun safe-offset (obj dist / res)
    (if (= dist 0.0)
      (vla-copy obj)
      (progn
        (setq res (vl-catch-all-apply 'vla-offset (list obj dist)))
        (if (not (vl-catch-all-error-p res))
          (car (vlax-safearray->list (vlax-variant-value res)))
          nil
        )
      )
    )
  )

  ;; Tinh toan offset theo can le
  (cond
    ((= side "Center") (setq d1 (/ w 2.0) d2 (/ w -2.0)))
    ((= side "Left")   (setq d1 0.0       d2 w))
    ((= side "Right")  (setq d1 0.0       d2 (- w)))
  )

  ;; Tao Layer Tiem Tuyen & Hatch neu chua co
  (if (not (tblsearch "LAYER" "DB-A-Wall")) (command "-layer" "m" "DB-A-Wall" "c" "2" "" ""))
  (if (not (tblsearch "LAYER" "DB-A-Hatch")) (command "-layer" "m" "DB-A-Hatch" "c" "252" "" ""))

  ;; Kiem tra va Load linetype HIDDEN neu ban ve chua co
  (if (not (tblobjname "LTYPE" "HIDDEN"))
    (vl-catch-all-apply 'vla-load (list (vla-get-Linetypes doc) "HIDDEN" "acad.lin"))
  )
  
  ;; Chuan bi Layer Tim An (Dung layer DB-A-Line Hidden mau so 8)
  (if (not (tblsearch "LAYER" "DB-A-Line Hidden")) 
    (command "-layer" "m" "DB-A-Line Hidden" "c" "8" "l" "HIDDEN" "" "")
  )

  (setq c1 (safe-offset obj d1))
  (setq c2 (safe-offset obj d2))

  (if (and c1 c2)
    (progn
      ;; [DA SUA LOI CUC KI QUAN TRONG]
      ;; Nhan dien duong khep kin cho ca truong hop go 'C' lan bat diem thu cong
      (setq isClosed nil)
      (if (or (= (vlax-curve-isClosed obj) T) 
              (equal (vlax-curve-getStartPoint obj) (vlax-curve-getEndPoint obj) 1e-4))
        (setq isClosed T)
      )

      (if isClosed
        ;; ----------------------------------------------------
        ;; TRUONG HOP TIM TUONG KHEP KIN (Dung 'C' hoac bat diem ve vach xuat phat)
        ;; ----------------------------------------------------
        (progn
          (vla-put-Layer c1 "DB-A-Wall")
          (vla-put-Layer c2 "DB-A-Wall")
          
          (setq a1 (vlax-curve-getArea c1))
          (setq a2 (vlax-curve-getArea c2))

          (setq hatchObj (vla-AddHatch space acHatchPatternTypePreDefined "SOLID" :vlax-true))
          (setq loop1 (vlax-make-safearray vlax-vbObject '(0 . 0)))
          (setq loop2 (vlax-make-safearray vlax-vbObject '(0 . 0)))

          ;; Net offset nao lon hon se lam Outer Loop, net nho hon lam Inner Loop (Khoet rong loi)
          (if (> a1 a2)
            (progn (vlax-safearray-put-element loop1 0 c1) (vlax-safearray-put-element loop2 0 c2))
            (progn (vlax-safearray-put-element loop1 0 c2) (vlax-safearray-put-element loop2 0 c1))
          )

          (if (not (vl-catch-all-error-p (vl-catch-all-apply 'vla-AppendOuterLoop (list hatchObj loop1))))
            (if (not (vl-catch-all-error-p (vl-catch-all-apply 'vla-AppendInnerLoop (list hatchObj loop2))))
              (progn
                (vla-Evaluate hatchObj)
                (vla-put-Layer hatchObj "DB-A-Hatch")
                (command "_.draworder" (vlax-vla-object->ename hatchObj) "" "B")
              )
            )
          )
        )
        ;; ----------------------------------------------------
        ;; TRUONG HOP TIM TUONG LA DUONG MO (VD: Nét hở chữ L, chữ U)
        ;; ----------------------------------------------------
        (progn
          (setq pt1 (vlax-curve-getStartPoint c1))
          (setq pt2 (vlax-curve-getEndPoint c1))
          (setq pt3 (vlax-curve-getStartPoint c2))
          (setq pt4 (vlax-curve-getEndPoint c2))

          ;; Ve 2 net bit dau tuong hở
          (setq l1 (vla-AddLine space (vlax-3d-point pt1) (vlax-3d-point pt3)))
          (setq l2 (vla-AddLine space (vlax-3d-point pt2) (vlax-3d-point pt4)))

          (setq e1 (vlax-vla-object->ename c1))
          (setq e2 (vlax-vla-object->ename c2))
          (setq el1 (vlax-vla-object->ename l1))
          (setq el2 (vlax-vla-object->ename l2))

          (setvar "PEDITACCEPT" 1)
          (command "_.pedit" "M" e1 e2 el1 el2 "" "J" "0.5" "")
          (setq bndEnt (entlast))

          (if (and bndEnt (wcmatch (cdr (assoc 0 (entget bndEnt))) "*POLYLINE"))
            (progn
              (setq bndObj (vlax-ename->vla-object bndEnt))
              (vla-put-Layer bndObj "DB-A-Wall")
              (if (vlax-property-available-p bndObj 'ConstantWidth)
                (vla-put-ConstantWidth bndObj 0.0)
              )

              (setq hatchObj (vla-AddHatch space acHatchPatternTypePreDefined "SOLID" :vlax-true))
              (setq loopObj (vlax-make-safearray vlax-vbObject '(0 . 0)))
              (vlax-safearray-put-element loopObj 0 bndObj)

              (if (not (vl-catch-all-error-p (vl-catch-all-apply 'vla-AppendOuterLoop (list hatchObj loopObj))))
                (progn
                  (vla-Evaluate hatchObj)
                  (vla-put-Layer hatchObj "DB-A-Hatch")
                  (command "_.draworder" (vlax-vla-object->ename hatchObj) "" "B")
                )
              )
            )
          )
        )
      )
      
      ;; ----------------------------------------------------
      ;; CHUYEN TIM SANG LAYER AN (DB-A-Line Hidden)
      ;; ----------------------------------------------------
      (vla-put-Layer obj "DB-A-Line Hidden")
      (vla-put-Linetype obj "BYLAYER")
    )
  )
)

;; ==========================================================================
;; LENH 1: [TW] - CHUYEN NET CO SAN THANH TUONG + HATCH
;; ==========================================================================
(defun c:TW ( / ss w side doc space i ent obj oldEcho oldPedit)
  (setq doc (vla-get-activedocument (vlax-get-acad-object)))
  (setq space (if (= (vla-get-activespace doc) 1) (vla-get-modelspace doc) (vla-get-paperspace doc)))
  (setq oldEcho (getvar "CMDECHO") oldPedit (getvar "PEDITACCEPT"))
  (setvar "CMDECHO" 0)

  (if (null *tw-width*) (setq *tw-width* 200.0))
  (setq w (getreal (strcat "\nNhap do day tuong <" (rtos *tw-width* 2 0) ">: ")))
  (if w (setq *tw-width* w) (setq w *tw-width*))

  (if (null *tw-side*) (setq *tw-side* "Center"))
  (initget "Left Center Right")
  (setq side (getkword (strcat "\nChon kieu can le [Left/Center/Right] <" *tw-side* ">: ")))
  (if side (setq *tw-side* side) (setq side *tw-side*))

  (princ "\nChon cac single line hoac Polyline de bien thanh tuong: ")
  (if (setq ss (ssget '((0 . "LINE,LWPOLYLINE,POLYLINE,ARC,CIRCLE"))))
    (progn
      (setq i 0)
      (while (< i (sslength ss))
        (setq ent (ssname ss i))
        (setq obj (vlax-ename->vla-object ent))
        (MakeWallFromCenter obj w *tw-side* doc space)
        (setq i (1+ i))
      )
      (princ "\n>> DONE! Da tao tuong thanh cong!")
    )
    (princ "\nKhong co doi tuong hop le!")
  )
  (setvar "CMDECHO" oldEcho) (if oldPedit (setvar "PEDITACCEPT" oldPedit))
  (princ)
)

;; ==========================================================================
;; LENH 2: [QQ] - VE TUONG LIEN TUC
;; ==========================================================================
(defun c:QQ ( / w side doc space oldEcho oldPedit startEnt newEnt obj)
  (setq doc (vla-get-activedocument (vlax-get-acad-object)))
  (setq space (if (= (vla-get-activespace doc) 1) (vla-get-modelspace doc) (vla-get-paperspace doc)))
  (setq oldEcho (getvar "CMDECHO") oldPedit (getvar "PEDITACCEPT"))
  
  (if (null *tw-width*) (setq *tw-width* 200.0))
  (setq w (getreal (strcat "\nNhap do day tuong <" (rtos *tw-width* 2 0) ">: ")))
  (if w (setq *tw-width* w) (setq w *tw-width*))

  (if (null *tw-side*) (setq *tw-side* "Center"))
  (initget "Left Center Right")
  (setq side (getkword (strcat "\nChon kieu can le [Left/Center/Right] <" *tw-side* ">: ")))
  (if side (setq *tw-side* side) (setq side *tw-side*))

  (setvar "CMDECHO" 1)
  (setq startEnt (entlast))

  ;; Goi lenh Polyline cho nguoi dung tu do ve hoac nhap 'C' de dong
  (princ "\n>> Click chuot de ve tim tuong (Hoac go C de khep kin). Nhan Enter de dung ve: ")
  (command "_.PLINE")
  
  (while (> (getvar 'CMDACTIVE) 0)
    (command "\\")
  )
  
  (setq newEnt (entlast))
  (if (and newEnt (not (equal startEnt newEnt)))
    (if (wcmatch (cdr (assoc 0 (entget newEnt))) "*POLYLINE,LINE")
      (progn
        (setvar "CMDECHO" 0)
        (setq obj (vlax-ename->vla-object newEnt))
        
        ;; Phat dong quy trinh
        (MakeWallFromCenter obj w *tw-side* doc space)
        (princ "\n>> DONE! Tuong dong da duoc tao thanh cong!")
      )
    )
  )

  (setvar "CMDECHO" oldEcho) (if oldPedit (setvar "PEDITACCEPT" oldPedit))
  (princ)
)