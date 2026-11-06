(vl-load-com)

;; ==========================================================================
;; LENH [ UO ] : XOAY MAN HINH BANG 2 DIEM (CHONG MAT BAT DIEM 100%)
;; ==========================================================================
(defun c:UO ( / *error* p1 p2 p3 dist zSize oldUcsFollow oldEcho oldOs)

  ;; 1. HAM BAY LOI: Tu dong tra lai bat diem (OSNAP) neu Lisp bi loi hoac nhan ESC
  (defun *error* (msg)
    (if oldOs (setvar "OSMODE" oldOs))
    (if oldUcsFollow (setvar "UCSFOLLOW" oldUcsFollow))
    (if oldEcho (setvar "CMDECHO" oldEcho))
    (princ "\n>> Lenh bi huy. Da khoi phuc lai bat diem (OSNAP) an toan!")
    (princ)
  )

  ;; Luu cac bien he thong truoc khi chay
  (setq oldEcho (getvar "CMDECHO"))
  (setvar "CMDECHO" 0)
  
  (setq oldUcsFollow (getvar "UCSFOLLOW"))
  (setvar "UCSFOLLOW" 0)

  (setq oldOs (getvar "OSMODE")) ;; Luu trang thai bat diem hien tai

  (if (setq p1 (getpoint "\n1. Click diem GOC (Tam man hinh): "))
    (if (setq p2 (getpoint p1 "\n2. Click huong truc X+ (Y+ ben trai): "))
      (progn
        ;; Tinh ty le Zoom
        (setq dist (distance p1 p2))
        (if (> dist 0.0)
          (setq zSize (* dist 2.5))
          (setq zSize (getvar "VIEWSIZE"))
        )
        
        ;; Tinh toa do truc Y+
        (setq p3 (polar p1 (+ (angle p1 p2) (/ pi 2.0)) 1.0))
        
        ;; Tat bat diem de chong loi Zoom vao cac net khac
        (setvar "OSMODE" 0)
        
        ;; Thuc thi lenh bang vl-cmdf de kiem soat loi chat che hon, chong hien kinh lup
        (vl-cmdf "_.UCS" "_3" p1 p2 p3)
        (vl-cmdf "_.PLAN" "_Current")
        (vl-cmdf "_.ZOOM" "_Center" '(0 0 0) zSize)
        
        ;; BAT LAI BAT DIEM NGAY LAP TUC
        (setvar "OSMODE" oldOs)
        
        (princ "\n>> XONG! Da xoay truc va Zoom tap trung dung vao vung vua click.")
      )
    )
  )
  
  ;; Tra lai moi truong neu Lisp chay tron tru den cuoi cung
  (if oldOs (setvar "OSMODE" oldOs))
  (if oldUcsFollow (setvar "UCSFOLLOW" oldUcsFollow))
  (if oldEcho (setvar "CMDECHO" oldEcho))
  (princ)
)

;; ==========================================================================
;; LENH [ UU ] : TRA VE WORLD VA ZOOM LAI MAN HINH CU
;; ==========================================================================
(defun c:UU ( / *error* viewCtr viewSize oldUcsFollow oldEcho oldOs)
  
  ;; Ham bay loi cho lenh UU
  (defun *error* (msg)
    (if oldOs (setvar "OSMODE" oldOs))
    (if oldUcsFollow (setvar "UCSFOLLOW" oldUcsFollow))
    (if oldEcho (setvar "CMDECHO" oldEcho))
    (princ)
  )

  (setq oldEcho (getvar "CMDECHO"))
  (setvar "CMDECHO" 0)
  
  (setq oldOs (getvar "OSMODE"))
  
  (setq viewCtr (trans (getvar "VIEWCTR") 1 0))
  (setq viewSize (getvar "VIEWSIZE"))
  
  (setq oldUcsFollow (getvar "UCSFOLLOW"))
  (setvar "UCSFOLLOW" 0)

  (setvar "OSMODE" 0)

  (vl-cmdf "_.UCS" "_World")
  (vl-cmdf "_.PLAN" "_World")

  (setq viewCtr (trans viewCtr 0 1))
  (vl-cmdf "_.ZOOM" "_Center" viewCtr viewSize)
  
  (setvar "OSMODE" oldOs)
  (setvar "UCSFOLLOW" oldUcsFollow)
  (setvar "CMDECHO" oldEcho)
  (princ "\n>> DA TRA VE TRUC TOA DO GOC (WORLD)!")
  (princ)
)